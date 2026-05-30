using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

public class TempCardSweepServiceTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> GetFirstLaneIdAsync()
        => await TestDataHelper.GetFirstLaneIdAsync(_client, _factory.DefaultBoardId);

    private async Task<Guid> CreateTempCardAsync()
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var payload = new
        {
            name = "Sweep Temp Card",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999),
        };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/cards/temp", payload);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestAuthHelper.JsonOptions);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddAttachmentAsync(Guid cardId)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "sweep-test.bin");

        var response = await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestAuthHelper.JsonOptions);
        return json.GetProperty("id").GetGuid();
    }

    // Back-dates a card's CreatedAtUtc directly, simulating a temp card that has been
    // sitting orphaned (the create-temp endpoint always stamps CreatedAtUtc = now).
    private async Task BackdateCreatedAtAsync(Guid cardId, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var card = await db.Cards.FindAsync(cardId);
        card.ShouldNotBeNull();
        card.CreatedAtUtc = createdAt;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sweep_RemovesAgedTempCard_KeepsFreshTempAndRealCard()
    {
        // Arrange — an aged temp card (with an attachment), a fresh temp card, and a real card.
        var agedTempId = await CreateTempCardAsync();
        await AddAttachmentAsync(agedTempId);
        await BackdateCreatedAtAsync(agedTempId, DateTimeOffset.UtcNow.AddHours(-3));

        var freshTempId = await CreateTempCardAsync();

        var realCardId = await CreateTempCardAsync();
        var finalizeResponse = await _client.PostAsync($"/api/v1/cards/{realCardId}/finalize", null);
        finalizeResponse.EnsureSuccessStatusCode();

        // Act — one sweep tick with a 1-hour TTL cutoff.
        int deleted;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
            deleted = await Api.TempCardSweepService.SweepAsync(db, cutoff, CancellationToken.None);
        }

        // Assert — exactly the aged temp card removed; fresh temp and real card survive.
        deleted.ShouldBe(1);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BoardDbContext>();

        (await verifyDb.Cards.AnyAsync(c => c.Id == agedTempId)).ShouldBeFalse();
        (await verifyDb.Cards.AnyAsync(c => c.Id == freshTempId)).ShouldBeTrue();
        (await verifyDb.Cards.AnyAsync(c => c.Id == realCardId)).ShouldBeTrue();

        // Cascade — the aged temp card's attachment is gone with it.
        (await verifyDb.Attachments.AnyAsync(a => a.CardId == agedTempId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Sweep_DoesNotDeleteAgedCardOnceFinalized()
    {
        // Arrange — a card created in the past but finalized (IsTemp = false). The sweep's
        // WHERE predicate filters on IsTemp, so an old-but-real card must survive. This is the
        // structural guard against the finalize-vs-sweep race: a card that flipped IsTemp=false
        // no longer matches the delete, even though its CreatedAtUtc is well past the cutoff.
        var cardId = await CreateTempCardAsync();
        var finalizeResponse = await _client.PostAsync($"/api/v1/cards/{cardId}/finalize", null);
        finalizeResponse.EnsureSuccessStatusCode();
        await BackdateCreatedAtAsync(cardId, DateTimeOffset.UtcNow.AddHours(-3));

        // Act
        int deleted;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);
            deleted = await Api.TempCardSweepService.SweepAsync(db, cutoff, CancellationToken.None);
        }

        // Assert — the finalized card is untouched.
        deleted.ShouldBe(0);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        (await verifyDb.Cards.AnyAsync(c => c.Id == cardId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Sweep_IsIdempotent_SecondRunDeletesNothing()
    {
        // Arrange — one aged temp card.
        var agedTempId = await CreateTempCardAsync();
        await BackdateCreatedAtAsync(agedTempId, DateTimeOffset.UtcNow.AddHours(-3));

        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        // Act — two consecutive sweeps over the same state.
        int firstDeleted;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
            firstDeleted = await Api.TempCardSweepService.SweepAsync(db, cutoff, CancellationToken.None);
        }

        int secondDeleted;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
            secondDeleted = await Api.TempCardSweepService.SweepAsync(db, cutoff, CancellationToken.None);
        }

        // Assert — first run removes the orphan, second run is a no-op.
        firstDeleted.ShouldBe(1);
        secondDeleted.ShouldBe(0);
    }
}

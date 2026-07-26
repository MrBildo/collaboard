using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

// The collision tests next door drive the history helper directly, which proves the retry resolves
// a collision but not that either entry point still goes through it. Removing the retry from both
// write paths leaves those tests — and the whole suite — green, so the fix for a release-critical
// concurrency defect can be deleted without anything noticing.
//
// These two close that seam by losing the race through the entry points themselves: an interceptor
// commits a rival edit in the gap between staging and save, so a write path that commits through
// the retry answers normally and one that commits through a plain save fails the request.
public class CardHistoryWiringTests(RevisionRaceFactory factory) : IClassFixture<RevisionRaceFactory>
{
    private readonly RevisionRaceFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RestDescriptionPatch_LosingTheRevisionRace_StillAnswersAndRecordsBothEdits()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Rest Wiring Race", "start");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Rest Wiring Rival", UserRole.HumanUser);

        _factory.Interceptor.Arm(cardId, rival.Id, "rival wording");

        try
        {
            // Act
            var response = await _client.PatchAsJsonAsync
            (
                $"/api/v1/cards/{cardId}",
                new { descriptionMarkdown = "my wording" }
            );

            // Assert — the request that lost the race is the one under test, so it has to have met
            // a real collision and still answered.
            _factory.Interceptor.HasFired.ShouldBeTrue();
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            _factory.Interceptor.Disarm();
        }

        await AssertBothEditsRecordedAsync(cardId, "rival wording", "my wording");
    }

    [Fact]
    public async Task McpUpdateCard_LosingTheRevisionRace_StillAnswersAndRecordsBothEdits()
    {
        // Arrange — the other write path, which shares the helper but reaches it on its own.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Mcp Wiring Race", "start");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Mcp Wiring Rival", UserRole.HumanUser);

        _factory.Interceptor.Arm(cardId, rival.Id, "rival wording");

        try
        {
            // Act
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
            var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
            var tools = new CardTools(db, new McpAuthService(new UserResolver(db)), broadcaster);

            var json = await tools.UpdateCardAsync
            (
                CollaboardApiFactory.TestAdminAuthKey,
                cardId: cardId,
                descriptionMarkdown: "my wording"
            );

            // Assert
            _factory.Interceptor.HasFired.ShouldBeTrue();
            json.ShouldNotStartWith("Error");
        }
        finally
        {
            _factory.Interceptor.Disarm();
        }

        await AssertBothEditsRecordedAsync(cardId, "rival wording", "my wording");
    }

    private async Task AssertBothEditsRecordedAsync(Guid cardId, string rivalValue, string ownValue)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();

        var rows = await db.CardFieldHistories
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Revision)
                .ToListAsync();

        // Both edits present, in commit order, on top of a seed written exactly once.
        rows.Select(r => r.Revision).ShouldBe([1, 2, 3]);
        rows.Select(r => r.Value).ShouldBe(["start", rivalValue, ownValue]);
        rows[0].EditedByUserId.ShouldBeNull();

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == cardId);
        card.DescriptionMarkdown.ShouldBe(ownValue);
    }

    private async Task<Guid> CreateCardAsync(string name, string descriptionMarkdown)
    {
        var laneId = await TestDataHelper.GetFirstLaneIdAsync(_client, _factory.DefaultBoardId);
        var response = await _client.PostAsJsonAsync
        (
            $"/api/v1/boards/{_factory.DefaultBoardId}/cards",
            new { name, laneId, descriptionMarkdown }
        );
        response.EnsureSuccessStatusCode();
        var card = await response.Content.ReadFromJsonAsync<JsonElement>(TestAuthHelper.JsonOptions);
        return card.GetProperty("id").GetGuid();
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Endpoints;
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

        _factory.Interceptor.Arm(cardId, rival.Id);

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

        await AssertBothEditsRecordedAsync(cardId, "rival edit 1", "my wording");
    }

    [Fact]
    public async Task McpUpdateCard_LosingTheRevisionRace_StillAnswersAndRecordsBothEdits()
    {
        // Arrange — the other write path, which shares the helper but reaches it on its own.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Mcp Wiring Race", "start");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Mcp Wiring Rival", UserRole.HumanUser);

        _factory.Interceptor.Arm(cardId, rival.Id);

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

        await AssertBothEditsRecordedAsync(cardId, "rival edit 1", "my wording");
    }

    [Fact]
    public async Task RestDescriptionPatch_LosingEveryRetryButTheLast_StillAnswers()
    {
        // Arm one collision short of the retry budget, so the write path loses on every attempt but
        // its last and still commits. The single-collision tests above never reach a second
        // iteration of the retry loop; this is where a loop that stopped rebuilding after one try —
        // or a retry deleted from the entry point — would surface.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Rest Last Attempt Race", "start");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Rest Last Attempt Rival", UserRole.HumanUser);

        _factory.Interceptor.Arm(cardId, rival.Id, CardHistoryHelper.MaxRevisionRetryAttempts - 1);

        try
        {
            // Act
            var response = await _client.PatchAsJsonAsync
            (
                $"/api/v1/cards/{cardId}",
                new { descriptionMarkdown = "my wording" }
            );

            // Assert — every collision but the last was met, and the request still answered.
            _factory.Interceptor.FiredCount.ShouldBe(CardHistoryHelper.MaxRevisionRetryAttempts - 1);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            _factory.Interceptor.Disarm();
        }

        // The own edit lands on top of the seed plus one revision per injected rival, and the seed is
        // written exactly once however many times the rows were rebuilt.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rows = await db.CardFieldHistories
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Revision)
                .ToListAsync();

        rows.Count.ShouldBe(CardHistoryHelper.MaxRevisionRetryAttempts + 1);
        rows.Count(r => r.Value == "start").ShouldBe(1);
        rows[0].EditedByUserId.ShouldBeNull();
        rows[^1].Value.ShouldBe("my wording");

        var card = await db.Cards.AsNoTracking().FirstAsync(c => c.Id == cardId);
        card.DescriptionMarkdown.ShouldBe("my wording");
    }

    [Fact]
    public async Task RestDescriptionPatch_ExhaustingEveryRetryAttempt_FailsTheRequest()
    {
        // Arm a collision for every attempt, so even the last is lost and the budget runs out. The
        // request fails (500) rather than hanging or silently dropping the edit, and the loop
        // terminates. The 500 on its own would not distinguish exhaustion from a retry-less
        // first-collision failure — the fired-count check is what proves the loop ran the full
        // budget before giving up, and it is why this reds too if the retry leaves the entry point.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Rest Exhaustion Race", "start");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Rest Exhaustion Rival", UserRole.HumanUser);

        _factory.Interceptor.Arm(cardId, rival.Id, CardHistoryHelper.MaxRevisionRetryAttempts);

        try
        {
            // Act
            var response = await _client.PatchAsJsonAsync
            (
                $"/api/v1/cards/{cardId}",
                new { descriptionMarkdown = "my wording" }
            );

            // Assert — every attempt met a collision, and the exhausted write failed the request.
            _factory.Interceptor.FiredCount.ShouldBe(CardHistoryHelper.MaxRevisionRetryAttempts);
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
        finally
        {
            _factory.Interceptor.Disarm();
        }
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

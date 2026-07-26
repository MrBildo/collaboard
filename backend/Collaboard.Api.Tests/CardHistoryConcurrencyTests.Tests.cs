using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

// Two editors saving one card's description at the same moment both allocate the same revision
// ordinal, and the unique index on it means the second one to reach the database is rejected.
//
// The race is reproduced deterministically rather than by running requests in parallel: the losing
// editor stages its rows, a rival commits in between on its own DbContext, and only then does the
// loser save. That is exactly the interleaving the live race produces, without two threads sharing
// the harness's single in-memory connection — which would fail on connection contention rather
// than on the collision under test, and only sometimes.
public class CardHistoryConcurrencyTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TwoEditsRacingTheFirstRevision_BothSucceed_AndTheTrailSeedsExactlyOnce()
    {
        // Arrange — a card with no trail yet, so both editors stage the seed row as well as their
        // own. This is the harder half of the race: the seed is the row a naive retry duplicates.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Description Race", "original");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Race Rival", UserRole.HumanUser);
        var loser = await TestAuthHelper.CreateUserAsync(_client, _factory, "Race Loser", UserRole.HumanUser);

        await using var loserScope = _factory.Services.CreateAsyncScope();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var loserCard = await loserDb.Cards.FindAsync(cardId);
        loserCard!.DescriptionMarkdown = "loser text";

        var loserChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            loserDb,
            cardId,
            "original",
            "loser text",
            loser.Id,
            DateTimeOffset.UtcNow
        );

        // The rival reads the same empty trail and commits first, taking revisions 1 and 2.
        await using var rivalScope = _factory.Services.CreateAsyncScope();
        var rivalDb = rivalScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rivalCard = await rivalDb.Cards.FindAsync(cardId);
        rivalCard!.DescriptionMarkdown = "rival text";

        var rivalChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            rivalDb,
            cardId,
            "original",
            "rival text",
            rival.Id,
            DateTimeOffset.UtcNow
        );

        await CardHistoryHelper.SaveWithRevisionRetryAsync(rivalDb, rivalChange);

        // Act — the loser's staged revisions 1 and 2 are both already taken.
        await CardHistoryHelper.SaveWithRevisionRetryAsync(loserDb, loserChange);

        // Assert — the losing edit landed instead of erroring, and landed as a real revision.
        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rows = await readDb.CardFieldHistories
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Revision)
                .ToListAsync();

        rows.Select(r => r.Revision).ShouldBe([1, 2, 3]);
        rows[0].Value.ShouldBe("original");
        rows[1].Value.ShouldBe("rival text");
        rows[2].Value.ShouldBe("loser text");

        // The load-bearing one. Rebuilding the staged rows re-runs the "is this trail empty?"
        // decision, so the seed is written once. A retry that merely renumbered what it had
        // staged would insert a second copy of "original" at revision 3, and the trail would
        // read as though the description had reverted before being changed again.
        rows.Count(r => r.Value == "original").ShouldBe(1);

        // Provenance survives the retry: the seed stays un-attributed, each edit keeps its editor.
        rows[0].EditedByUserId.ShouldBeNull();
        rows[1].EditedByUserId.ShouldBe(rival.Id);
        rows[2].EditedByUserId.ShouldBe(loser.Id);

        var card = await readDb.Cards.AsNoTracking().FirstAsync(c => c.Id == cardId);
        card.DescriptionMarkdown.ShouldBe("loser text");
    }

    [Fact]
    public async Task AnEditRacingALaterRevision_LandsOnTopWithoutReseeding()
    {
        // Arrange — a card whose trail already exists, so neither editor stages a seed row and the
        // collision is purely over the next ordinal.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Description Race Later", "first");
        await PatchDescriptionAsync(cardId, "second");

        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Later Rival", UserRole.HumanUser);
        var loser = await TestAuthHelper.CreateUserAsync(_client, _factory, "Later Loser", UserRole.HumanUser);

        await using var loserScope = _factory.Services.CreateAsyncScope();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var loserCard = await loserDb.Cards.FindAsync(cardId);
        loserCard!.DescriptionMarkdown = "loser third";

        var loserChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            loserDb,
            cardId,
            "second",
            "loser third",
            loser.Id,
            DateTimeOffset.UtcNow
        );

        await using var rivalScope = _factory.Services.CreateAsyncScope();
        var rivalDb = rivalScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rivalChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            rivalDb,
            cardId,
            "second",
            "rival third",
            rival.Id,
            DateTimeOffset.UtcNow
        );

        await CardHistoryHelper.SaveWithRevisionRetryAsync(rivalDb, rivalChange);

        // Act
        await CardHistoryHelper.SaveWithRevisionRetryAsync(loserDb, loserChange);

        // Assert — four revisions, dense and in commit order, nothing lost and nothing duplicated.
        await using var readScope = _factory.Services.CreateAsyncScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rows = await readDb.CardFieldHistories
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Revision)
                .ToListAsync();

        rows.Select(r => r.Revision).ShouldBe([1, 2, 3, 4]);
        rows.Select(r => r.Value).ShouldBe(["first", "second", "rival third", "loser third"]);
    }

    [Fact]
    public async Task TheRetriedEditIsVisibleThroughTheReadSurface()
    {
        // The rows being right is necessary but not the promise; the promise is that a reader sees
        // both edits attributed, with a diff from one to the other.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("Race Readback", "alpha");
        var rival = await TestAuthHelper.CreateUserAsync(_client, _factory, "Readback Rival", UserRole.HumanUser);
        var loser = await TestAuthHelper.CreateUserAsync(_client, _factory, "Readback Loser", UserRole.HumanUser);

        await using var loserScope = _factory.Services.CreateAsyncScope();
        var loserDb = loserScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var loserCard = await loserDb.Cards.FindAsync(cardId);
        loserCard!.DescriptionMarkdown = "gamma";

        var loserChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            loserDb,
            cardId,
            "alpha",
            "gamma",
            loser.Id,
            DateTimeOffset.UtcNow
        );

        await using var rivalScope = _factory.Services.CreateAsyncScope();
        var rivalDb = rivalScope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rivalChange = await CardHistoryHelper.StageDescriptionChangeAsync
        (
            rivalDb,
            cardId,
            "alpha",
            "beta",
            rival.Id,
            DateTimeOffset.UtcNow
        );

        await CardHistoryHelper.SaveWithRevisionRetryAsync(rivalDb, rivalChange);
        await CardHistoryHelper.SaveWithRevisionRetryAsync(loserDb, loserChange);

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history");
        response.EnsureSuccessStatusCode();
        var trail = await response.Content.ReadFromJsonAsync<JsonElement>(TestAuthHelper.JsonOptions);

        // Assert — three revisions, and the newest diffs against the one that beat it.
        trail.GetProperty("totalCount").GetInt32().ShouldBe(3);
        var entries = trail.GetProperty("entries").EnumerateArray().ToArray();
        entries[0].GetProperty("value").GetString().ShouldBe("gamma");
        entries[0].GetProperty("diff").GetString().ShouldBe("@@ -1,1 +1,1 @@\n-beta\n+gamma\n");
        entries[1].GetProperty("value").GetString().ShouldBe("beta");
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

    private async Task PatchDescriptionAsync(Guid cardId, string descriptionMarkdown)
    {
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { descriptionMarkdown });
        response.EnsureSuccessStatusCode();
    }
}

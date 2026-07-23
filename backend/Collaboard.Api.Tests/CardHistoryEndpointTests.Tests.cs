using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

public class CardHistoryEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static JsonSerializerOptions JsonOptions => TestAuthHelper.JsonOptions;

    [Fact]
    public async Task EditDescription_RecordsThePriorValueWithEditorAndTimestamp()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Capture", "original text");

        // Act
        await PatchDescriptionAsync(cardId, "replacement text");

        // Assert — two revisions: the value that was already there, then the one that replaced it.
        var entries = await GetTrailEntriesAsync(cardId);
        entries.Length.ShouldBe(2);

        var newest = entries[0];
        newest.GetProperty("revision").GetInt32().ShouldBe(2);
        newest.GetProperty("value").GetString().ShouldBe("replacement text");
        newest.GetProperty("editedByName").GetString().ShouldBe("Admin");
        newest.GetProperty("editedAtUtc").ValueKind.ShouldNotBe(JsonValueKind.Null);

        var oldest = entries[1];
        oldest.GetProperty("revision").GetInt32().ShouldBe(1);
        oldest.GetProperty("value").GetString().ShouldBe("original text");
        oldest.GetProperty("diff").GetString().ShouldBe(string.Empty);

        // The trail's oldest value predates recording, so its provenance is admitted as unknown
        // rather than attributed to the card's creator or to whoever triggered the capture.
        oldest.GetProperty("editedByUserId").ValueKind.ShouldBe(JsonValueKind.Null);
        oldest.GetProperty("editedAtUtc").ValueKind.ShouldBe(JsonValueKind.Null);

        // Nothing was lost and the card still carries the new text.
        var card = await GetCardAsync(cardId);
        card.GetProperty("card").GetProperty("descriptionMarkdown").GetString().ShouldBe("replacement text");
    }

    [Fact]
    public async Task EditDescriptionTwice_DiffFormat_ReturnsAUnifiedDiffPerRevisionNewestFirst()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Diff", "alpha\nbeta");

        await PatchDescriptionAsync(cardId, "alpha\ngamma");
        await PatchDescriptionAsync(cardId, "alpha\ngamma\ndelta");

        // Act
        var entries = await GetTrailEntriesAsync(cardId, "format=diff");

        // Assert — newest first, one diff per edit, and no full snapshots on the wire.
        entries.Select(e => e.GetProperty("revision").GetInt32()).ShouldBe([3, 2, 1]);
        entries.Any(e => e.TryGetProperty("value", out _)).ShouldBeFalse();

        entries[0].GetProperty("diff").GetString().ShouldBe("@@ -1,2 +1,3 @@\n alpha\n gamma\n+delta\n");
        entries[1].GetProperty("diff").GetString().ShouldBe("@@ -1,2 +1,2 @@\n alpha\n-beta\n+gamma\n");
        entries[2].GetProperty("diff").GetString().ShouldBe(string.Empty);
    }

    [Fact]
    public async Task FormatFull_ReturnsWholeValuesAndOmitsDiffs()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Full", "before");
        await PatchDescriptionAsync(cardId, "after");

        // Act
        var entries = await GetTrailEntriesAsync(cardId, "format=full");

        // Assert
        entries[0].GetProperty("value").GetString().ShouldBe("after");
        entries[1].GetProperty("value").GetString().ShouldBe("before");
        entries.Any(e => e.TryGetProperty("diff", out _)).ShouldBeFalse();
    }

    [Fact]
    public async Task RestDefaultFormat_IsBoth()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Default Format", "before");
        await PatchDescriptionAsync(cardId, "after");

        // Act — no format parameter
        var entries = await GetTrailEntriesAsync(cardId);

        // Assert
        entries[0].GetProperty("value").GetString().ShouldBe("after");
        entries[0].GetProperty("diff").GetString()!.ShouldContain("+after");
    }

    [Fact]
    public async Task ArbitraryPair_ReturnsTheDiffBetweenTwoRevisions()
    {
        // Arrange — three versions
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Pair", "one");
        await PatchDescriptionAsync(cardId, "two");
        await PatchDescriptionAsync(cardId, "three");

        // Act — skip the middle revision
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?from=1&to=3");

        // Assert
        response.EnsureSuccessStatusCode();
        var pair = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        pair.GetProperty("from").GetInt32().ShouldBe(1);
        pair.GetProperty("to").GetInt32().ShouldBe(3);
        pair.GetProperty("diff").GetString().ShouldBe("@@ -1,1 +1,1 @@\n-one\n+three\n");
        pair.GetProperty("fromValue").GetString().ShouldBe("one");
        pair.GetProperty("toValue").GetString().ShouldBe("three");
    }

    [Fact]
    public async Task ArbitraryPair_ReversedOrder_ReturnsTheUndoingDiff()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Reverse Pair", "one");
        await PatchDescriptionAsync(cardId, "two");

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?from=2&to=1");

        // Assert
        response.EnsureSuccessStatusCode();
        var pair = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        pair.GetProperty("diff").GetString().ShouldBe("@@ -1,1 +1,1 @@\n-two\n+one\n");
    }

    [Fact]
    public async Task NoOpDescriptionSave_RecordsNoEntry()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History No-Op", "unchanged");

        // Act — save the description exactly as it already is
        await PatchDescriptionAsync(cardId, "unchanged");

        // Assert
        var entries = await GetTrailEntriesAsync(cardId);
        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoOpDescriptionSave_AfterARealEdit_AddsNoFurtherEntry()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History No-Op After Edit", "first");
        await PatchDescriptionAsync(cardId, "second");

        // Act
        await PatchDescriptionAsync(cardId, "second");

        // Assert
        var entries = await GetTrailEntriesAsync(cardId);
        entries.Length.ShouldBe(2);
    }

    [Fact]
    public async Task PatchThatLeavesTheDescriptionAlone_RecordsNoEntry()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Lane Only", "untouched");
        var targetLaneId = await TestDataHelper.GetLaneIdByIndexAsync(_client, _factory.DefaultBoardId, 1);

        // Act — a lane move carries no description at all
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { laneId = targetLaneId });
        response.EnsureSuccessStatusCode();

        // Assert
        var entries = await GetTrailEntriesAsync(cardId);
        entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task ArchivedCard_RejectsTheEditAndKeepsExistingHistoryReadable()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Archived", "before archiving");
        await PatchDescriptionAsync(cardId, "edited before archiving");

        var archiveResponse = await _client.PostAsync($"/api/v1/cards/{cardId}/archive", null);
        archiveResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        var patchResponse = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { descriptionMarkdown = "not allowed" });

        // Assert — the edit is refused and accrues nothing, but the trail is still readable.
        patchResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var entries = await GetTrailEntriesAsync(cardId);
        entries.Length.ShouldBe(2);
        entries[0].GetProperty("value").GetString().ShouldBe("edited before archiving");
    }

    [Fact]
    public async Task NeverEditedCard_ReturnsAnEmptyTrail()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Never Edited", "as created");

        // Act
        var entries = await GetTrailEntriesAsync(cardId);

        // Assert — no synthetic first entry; the current text is still on the card.
        entries.ShouldBeEmpty();

        var card = await GetCardAsync(cardId);
        card.GetProperty("card").GetProperty("descriptionMarkdown").GetString().ShouldBe("as created");
    }

    [Fact]
    public async Task History_WithoutAuthentication_IsDeniedLikeTheCardRead()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Unauthorized", "secret");

        using var anonymous = _factory.CreateClient();

        // Act
        var historyResponse = await anonymous.GetAsync($"/api/v1/cards/{cardId}/history");
        var cardResponse = await anonymous.GetAsync($"/api/v1/cards/{cardId}");

        // Assert — history is exactly as reachable as the card it belongs to.
        historyResponse.StatusCode.ShouldBe(cardResponse.StatusCode);
        historyResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task History_IsReadableByANonAdminUser()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Agent Read", "before");
        await PatchDescriptionAsync(cardId, "after");

        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "History Reader", UserRole.AgentUser);

        using var agentClient = _factory.CreateClient();
        TestAuthHelper.SetAuth(agentClient, agent.AuthKey);

        // Act
        var response = await agentClient.GetAsync($"/api/v1/cards/{cardId}/history");

        // Assert — reading history needs no role beyond reading the card.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var trail = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        trail.GetProperty("entries").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task History_ForAnUnknownCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{Guid.NewGuid()}/history");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task History_WithAnUnknownField_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Bad Field", "text");

        // Act — a typo must not read as "this card has no history"
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?field=descriptoin");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_WithAnUnknownFormat_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Bad Format", "text");

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?format=patch");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_WithFromButNoTo_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Half Pair", "text");
        await PatchDescriptionAsync(cardId, "edited");

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?from=1");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_PairNamingAMissingRevision_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Missing Revision", "text");
        await PatchDescriptionAsync(cardId, "edited");

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history?from=1&to=99");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RestAndMcpEdits_ShareOneTrailWithPerEditAttribution()
    {
        // Arrange — the two description write paths must record through the same seam, so an edit
        // from either surface continues the same trail rather than starting a parallel one.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Cross Surface", "genesis");
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "History Agent", UserRole.AgentUser);

        await PatchDescriptionAsync(cardId, "edited over rest");

        // Act — second edit through MCP, as a different user
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
        var cardTools = new CardTools(db, authService, broadcaster);

        await cardTools.UpdateCardAsync(agent.AuthKey, cardId: cardId, descriptionMarkdown: "edited over mcp");

        // Assert — one continuous trail, each revision attributed to whoever made that edit.
        var entries = await GetTrailEntriesAsync(cardId, "format=full");
        entries.Select(e => e.GetProperty("revision").GetInt32()).ShouldBe([3, 2, 1]);
        entries[0].GetProperty("editedByName").GetString().ShouldBe("History Agent");
        entries[1].GetProperty("editedByName").GetString().ShouldBe("Admin");
        entries[2].GetProperty("editedByName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task DeletingACard_CascadesToItsHistoryRows()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Cascade", "before");
        await PatchDescriptionAsync(cardId, "after");

        // Act
        var deleteResponse = await _client.DeleteAsync($"/api/v1/cards/{cardId}");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Assert — asserted against the rows themselves, not a route's status code.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var remaining = await db.CardFieldHistories.CountAsync(h => h.CardId == cardId);
        remaining.ShouldBe(0);
    }

    [Fact]
    public async Task GenesisRevision_RoundTripsItsNullTimestampThroughThePersistenceLayer()
    {
        // Arrange — the shared DateTimeOffset value converter is applied to a nullable column here
        // for the first time in the model; a null that failed to round-trip would surface as a
        // read-time exception rather than a wrong answer.
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var cardId = await CreateCardAsync("History Null Timestamp", "before");
        await PatchDescriptionAsync(cardId, "after");

        // Act
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var rows = await db.CardFieldHistories
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Revision)
                .ToListAsync();

        // Assert
        rows[0].EditedAtUtc.ShouldBeNull();
        rows[0].EditedByUserId.ShouldBeNull();
        rows[1].EditedAtUtc.ShouldNotBeNull();
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
        var card = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return card.GetProperty("id").GetGuid();
    }

    private async Task PatchDescriptionAsync(Guid cardId, string descriptionMarkdown)
    {
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { descriptionMarkdown });
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> GetCardAsync(Guid cardId)
    {
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<JsonElement[]> GetTrailEntriesAsync(Guid cardId, string? query = null)
    {
        var suffix = query is null ? string.Empty : $"?{query}";
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/history{suffix}");
        response.EnsureSuccessStatusCode();
        var trail = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return [.. trail.GetProperty("entries").EnumerateArray()];
    }
}

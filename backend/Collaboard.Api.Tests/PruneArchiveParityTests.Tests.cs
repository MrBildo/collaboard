using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

// Cross-surface parity tests for the prune archive-loop (#267 D6, #206 testing
// convention). REST POST /boards/{id}/prune (archive action) and the MCP prune tool
// now route through the shared PruneArchiveHelper, so the same filter must archive
// the same cards on both surfaces, and the no-archive-lane failure must be reported
// the same way (REST 400, MCP "Error: ..." string).
public class PruneArchiveParityTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>, IDisposable
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<IServiceScope> _scopes = [];

    private (BoardDbContext Db, PruneTools Prune) CreateMcpTools()
    {
        var scope = _factory.Services.CreateScope();
        _scopes.Add(scope);
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
        return (db, new PruneTools(db, auth, broadcaster));
    }

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // Spins up a fresh board with one non-archive lane carrying `cardCount` cards.
    // Returns the board id and the lane id so a prune can target the lane.
    private async Task<(Guid BoardId, Guid LaneId)> SeedBoardWithCardsAsync(int cardCount)
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = $"Prune Parity {Guid.NewGuid():N}" });
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var boardId = board.GetProperty("id").GetGuid();

        var laneResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{boardId}/lanes", new { name = "Work", position = 0 });
        laneResponse.EnsureSuccessStatusCode();
        var lane = await laneResponse.Content.ReadFromJsonAsync<JsonElement>();
        var laneId = lane.GetProperty("id").GetGuid();

        for (var i = 0; i < cardCount; i++)
        {
            var cardName = $"Card {i.ToString(CultureInfo.InvariantCulture)}";
            var cardResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{boardId}/cards", new { name = cardName, laneId });
            cardResponse.EnsureSuccessStatusCode();
        }

        return (boardId, laneId);
    }

    private async Task<int> ArchivedCountAsync(Guid boardId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var archiveLaneIds = await db.Lanes
            .Where(l => l.BoardId == boardId && l.IsArchiveLane)
                .Select(l => l.Id)
                    .ToListAsync();

        return await db.Cards.CountAsync(c => c.BoardId == boardId && archiveLaneIds.Contains(c.LaneId));
    }

    [Fact]
    public async Task PruneArchive_SameFilter_ArchivesSameOnBothSurfaces()
    {
        // Arrange — two equivalent boards, three cards each in one lane
        var (restBoardId, restLaneId) = await SeedBoardWithCardsAsync(3);
        var (mcpBoardId, mcpLaneId) = await SeedBoardWithCardsAsync(3);
        var (_, prune) = CreateMcpTools();

        // Act — REST prune-archive by lane
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var restResponse = await _client.PostAsJsonAsync
        (
            $"/api/v1/boards/{restBoardId}/prune",
            new { laneIds = new[] { restLaneId } }
        );

        // Act — MCP prune by lane
        var mcpResult = await prune.PruneAsync(_factory.AdminAuthKey, mcpBoardId, laneIds: mcpLaneId.ToString());

        // Assert — both report 3 archived, and both boards have 3 cards in the archive lane
        restResponse.EnsureSuccessStatusCode();
        var restBody = await restResponse.Content.ReadFromJsonAsync<JsonElement>();
        restBody.GetProperty("archivedCount").GetInt32().ShouldBe(3);

        JsonSerializer.Deserialize<JsonElement>(mcpResult).GetProperty("archivedCount").GetInt32().ShouldBe(3);

        (await ArchivedCountAsync(restBoardId)).ShouldBe(3);
        (await ArchivedCountAsync(mcpBoardId)).ShouldBe(3);
    }
}

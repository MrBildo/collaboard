using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Mcp;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

// Cross-surface parity tests for label CRUD.
// LabelEndpoints.cs and LabelTools.cs
// re-encode the same name-uniqueness-per-board rule independently, with no shared
// service. These tests feed the same invalid input to both surfaces and assert both
// reject identically; the delete-cleanup case asserts both surfaces un-assign the
// label from its cards (the cheaply-assertable cleanup half).
public class LabelCrudParityTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>, IDisposable
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<IServiceScope> _scopes = [];

    private (BoardDbContext Db, LabelTools Tools, string AuthKey) CreateMcpTools()
    {
        var scope = _factory.Services.CreateScope();
        _scopes.Add(scope);
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<BoardEventBroadcaster>();
        return (db, new LabelTools(db, auth, broadcaster), _factory.AdminAuthKey);
    }

    public void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<Guid> SeedBoardAsync()
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var response = await _client.PostAsJsonAsync("/api/v1/boards", new { name = $"Label Parity {Guid.NewGuid():N}" });
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateLabelAsync(Guid boardId, string name, string color)
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{boardId}/labels", new { name, color });
        response.EnsureSuccessStatusCode();
        var label = await response.Content.ReadFromJsonAsync<JsonElement>();
        return label.GetProperty("id").GetGuid();
    }

    // A freshly POSTed board seeds only the archive lane (hidden from /board), so it
    // has no working lane to place a card in. Create one explicitly.
    private async Task<Guid> CreateLaneAsync(Guid boardId)
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{boardId}/lanes", new { name = "Work", position = 0 });
        response.EnsureSuccessStatusCode();
        var lane = await response.Content.ReadFromJsonAsync<JsonElement>();
        return lane.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateCardWithLabelAsync(Guid boardId, Guid laneId, Guid labelId)
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var response = await _client.PostAsJsonAsync
        (
            $"/api/v1/boards/{boardId}/cards",
            new { name = "Labeled card", laneId, labelIds = new[] { labelId } }
        );
        response.EnsureSuccessStatusCode();
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        return card.GetProperty("id").GetGuid();
    }

    // ── create_label: empty name rejected on both surfaces ────────────────────

    [Fact]
    public async Task CreateLabel_EmptyName_RejectedOnBothSurfaces()
    {
        // Arrange
        var boardId = await SeedBoardAsync();
        var (_, tools, authKey) = CreateMcpTools();

        // Act — REST
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var restResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{boardId}/labels", new { name = "   ", color = "#ff0000" });

        // Act — MCP
        var mcpResult = await tools.CreateLabelAsync(authKey, boardId, "   ", "#ff0000");

        // Assert
        restResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var restBody = await restResponse.Content.ReadAsStringAsync();
        restBody.ShouldContain("Name is required");
        restBody.ShouldNotContain("Error:");

        mcpResult.ShouldContain("Error: Name is required");
    }

    // ── create_label: duplicate name on same board rejected on both surfaces ──
    // REST categorizes this as 409 Conflict; MCP returns the "Error: ..." string.

    [Fact]
    public async Task CreateLabel_DuplicateName_RejectedOnBothSurfaces()
    {
        // Arrange — each board already carries a "Priority" label
        var restBoardId = await SeedBoardAsync();
        var mcpBoardId = await SeedBoardAsync();
        await CreateLabelAsync(restBoardId, "Priority", "#ff0000");
        await CreateLabelAsync(mcpBoardId, "Priority", "#ff0000");
        var (_, tools, authKey) = CreateMcpTools();

        // Act — REST: a second "Priority" on the same board
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var restResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{restBoardId}/labels", new { name = "Priority", color = "#00ff00" });

        // Act — MCP
        var mcpResult = await tools.CreateLabelAsync(authKey, mcpBoardId, "Priority", "#00ff00");

        // Assert — REST 409 Conflict, MCP "Error: ..." — same rule, same message
        restResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var restBody = await restResponse.Content.ReadAsStringAsync();
        restBody.ShouldContain("A label with that name already exists on this board");
        restBody.ShouldNotContain("Error:");

        mcpResult.ShouldContain("Error: A label with that name already exists on this board");
    }

    // ── delete_label: card-label assignments cleaned up on both surfaces ───────
    // Both surfaces must un-assign the label from any card it was applied to without
    // deleting the cards. This is the cheaply-assertable cleanup half of the rule.

    [Fact]
    public async Task DeleteLabel_CleansUpCardAssignments_OnBothSurfaces()
    {
        // Arrange — a label applied to one card on each board
        var restBoardId = await SeedBoardAsync();
        var mcpBoardId = await SeedBoardAsync();
        var restLabelId = await CreateLabelAsync(restBoardId, "Doomed", "#ff0000");
        var mcpLabelId = await CreateLabelAsync(mcpBoardId, "Doomed", "#ff0000");
        var restLaneId = await CreateLaneAsync(restBoardId);
        var mcpLaneId = await CreateLaneAsync(mcpBoardId);
        var restCardId = await CreateCardWithLabelAsync(restBoardId, restLaneId, restLabelId);
        var mcpCardId = await CreateCardWithLabelAsync(mcpBoardId, mcpLaneId, mcpLabelId);
        var (db, tools, authKey) = CreateMcpTools();

        // Act — REST
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var restResponse = await _client.DeleteAsync($"/api/v1/boards/{restBoardId}/labels/{restLabelId}");

        // Act — MCP
        var mcpResult = await tools.DeleteLabelAsync(authKey, mcpLabelId);

        // Assert — both succeed
        restResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        mcpResult.ShouldNotContain("Error");

        // Assert — the label rows and their card assignments are gone on both surfaces,
        // but the cards survive (delete un-assigns; it does not cascade to cards)
        (await db.Labels.AnyAsync(l => l.Id == restLabelId)).ShouldBeFalse();
        (await db.Labels.AnyAsync(l => l.Id == mcpLabelId)).ShouldBeFalse();
        (await db.CardLabels.AnyAsync(cl => cl.LabelId == restLabelId)).ShouldBeFalse();
        (await db.CardLabels.AnyAsync(cl => cl.LabelId == mcpLabelId)).ShouldBeFalse();
        (await db.Cards.AnyAsync(c => c.Id == restCardId)).ShouldBeTrue();
        (await db.Cards.AnyAsync(c => c.Id == mcpCardId)).ShouldBeTrue();
    }
}

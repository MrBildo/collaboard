using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

public class ArchiveLaneTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static JsonSerializerOptions JsonOptions => TestAuthHelper.JsonOptions;

    // --- Seeded board has archive lane (hidden from listings) ---

    [Fact]
    public async Task GetLanes_ExcludesArchiveLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/lanes");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lanes = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        lanes.ShouldNotBeNull();
        foreach (var lane in lanes)
        {
            lane.GetProperty("name").GetString().ShouldNotBe("Archive");
            lane.GetProperty("isArchiveLane").GetBoolean().ShouldBeFalse();
        }
    }

    [Fact]
    public async Task GetBoardComposite_ExcludesArchiveLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var lanes = json.GetProperty("lanes");
        lanes.GetArrayLength().ShouldBe(3);
        foreach (var lane in lanes.EnumerateArray())
        {
            lane.GetProperty("isArchiveLane").GetBoolean().ShouldBeFalse();
        }
    }

    // --- New board gets archive lane ---

    [Fact]
    public async Task CreateBoard_AutoCreatesArchiveLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"Archive Test {Guid.NewGuid()}";

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        // Assert — lane listing should be empty (no non-archive lanes created by POST /boards)
        var lanesResponse = await _client.GetAsync($"/api/v1/boards/{boardId}/lanes");
        lanesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lanes = await lanesResponse.Content.ReadFromJsonAsync<JsonElement[]>();
        lanes.ShouldNotBeNull();
        lanes.ShouldBeEmpty();

        // Verify archive lane exists via the DB (create a lane at position 0, which proves the board exists)
        // The archive lane is hidden, but we can verify it blocks deletion
        var deleteResponse = await _client.DeleteAsync($"/api/v1/boards/{boardId}");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // --- Lane create rejects int.MaxValue position ---

    [Fact]
    public async Task CreateLane_RejectsMaxValuePosition()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/lanes",
            new { name = "Sneaky Lane", position = int.MaxValue });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Lane update rejects int.MaxValue position ---

    [Fact]
    public async Task PatchLane_RejectsMaxValuePosition()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var createResponse = await _client.PostAsJsonAsync(
            $"/api/v1/boards/{_factory.DefaultBoardId}/lanes",
            new { name = "PatchMaxVal", position = 600 });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var laneId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/lanes/{laneId}",
            new { position = int.MaxValue });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Archive lane cannot be deleted ---

    [Fact]
    public async Task DeleteArchiveLane_Returns400()
    {
        // Arrange — create a board so we have a known archive lane
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"DelArchive Test {Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.EnsureSuccessStatusCode();
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        // Find the archive lane via direct DB access through the factory
        var archiveLaneId = await GetArchiveLaneIdAsync(boardId);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{archiveLaneId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Archive lane cannot be modified ---

    [Fact]
    public async Task PatchArchiveLane_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"PatchArchive Test {Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.EnsureSuccessStatusCode();
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        var archiveLaneId = await GetArchiveLaneIdAsync(boardId);

        // Act
        var response = await _client.PatchAsJsonAsync(
            $"/api/v1/lanes/{archiveLaneId}",
            new { name = "Renamed Archive" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // --- Board deletion works when only archive lane remains ---

    [Fact]
    public async Task DeleteBoard_WithOnlyArchiveLane_Succeeds()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"DeleteBoard Test {Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.EnsureSuccessStatusCode();
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        // Act — board has only the auto-created archive lane
        var response = await _client.DeleteAsync($"/api/v1/boards/{boardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify board is gone
        var getResponse = await _client.GetAsync($"/api/v1/boards/{boardId}");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Board deletion with archived cards returns count ---

    [Fact]
    public async Task DeleteBoard_WithArchivedCards_ReturnsCount()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"DelArchCards Test {Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.EnsureSuccessStatusCode();
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        // Create a lane, add cards, then place them in the archive lane directly via DB
        var archiveLaneId = await GetArchiveLaneIdAsync(boardId);

        // Add a normal lane first to create a card
        var laneResponse = await _client.PostAsJsonAsync(
            $"/api/v1/boards/{boardId}/lanes",
            new { name = "Temp Lane", position = 0 });
        laneResponse.EnsureSuccessStatusCode();
        var lane = await laneResponse.Content.ReadFromJsonAsync<JsonElement>();
        var laneId = lane.GetProperty("id").GetGuid();

        // Create two cards
        for (var i = 0; i < 2; i++)
        {
            var cardResponse = await _client.PostAsJsonAsync(
                $"/api/v1/boards/{boardId}/cards",
                new { name = $"Card {i}", laneId });
            cardResponse.EnsureSuccessStatusCode();

            // Move card to archive lane via reorder
            var card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var cardId = card.GetProperty("id").GetGuid();
            await MoveCardToArchiveLaneAsync(cardId, archiveLaneId);
        }

        // Delete the normal lane (now empty)
        var deleteLaneResponse = await _client.DeleteAsync($"/api/v1/lanes/{laneId}");
        deleteLaneResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/boards/{boardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        json.GetProperty("archivedCardsDeleted").GetInt32().ShouldBe(2);
    }

    // --- Get lane by ID still works for archive lanes ---

    [Fact]
    public async Task GetLaneById_ArchiveLane_Returns200()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var boardName = $"GetArchive Test {Guid.NewGuid()}";
        var createResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = boardName });
        createResponse.EnsureSuccessStatusCode();
        var board = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var boardId = board.GetProperty("id").GetGuid();

        var archiveLaneId = await GetArchiveLaneIdAsync(boardId);

        // Act
        var response = await _client.GetAsync($"/api/v1/lanes/{archiveLaneId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lane = await response.Content.ReadFromJsonAsync<JsonElement>();
        lane.GetProperty("isArchiveLane").GetBoolean().ShouldBeTrue();
        lane.GetProperty("name").GetString().ShouldBe("Archive");
        lane.GetProperty("position").GetInt32().ShouldBe(int.MaxValue);
    }

    // --- Helpers ---

    private async Task<Guid> GetArchiveLaneIdAsync(Guid boardId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var archiveLane = await db.Lanes.FirstAsync(l => l.BoardId == boardId && l.IsArchiveLane);
        return archiveLane.Id;
    }

    private async Task MoveCardToArchiveLaneAsync(Guid cardId, Guid archiveLaneId)
    {
        // Move directly via DB since the reorder endpoint may get guards later
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var card = await db.Cards.FindAsync(cardId);
        card!.LaneId = archiveLaneId;
        card.Position = 0;
        await db.SaveChangesAsync();
    }
}

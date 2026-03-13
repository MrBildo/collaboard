using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class CardEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private static int _nextPosition = 1000;

    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;

    public CardEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static int NextPosition() => Interlocked.Increment(ref _nextPosition);

    private async Task<Guid> GetFirstLaneIdAsync()
    {
        var response = await _client.GetAsync("/api/v1/board");
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("lanes")[0].GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetLaneIdByIndexAsync(int index)
    {
        var response = await _client.GetAsync("/api/v1/board");
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("lanes")[index].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task PostCard_AsHumanUser_Returns201WithAutoNumberAndTimestamps()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanCardCreator", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, user.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "My First Card",
            descriptionMarkdown = "Some description",
            status = "Open",
            size = "L",
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/cards", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(card.GetProperty("number").GetInt64() > 0);
        Assert.Equal(user.Id, card.GetProperty("createdByUserId").GetGuid());
        Assert.NotEqual(default, card.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.NotEqual(default, card.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task PostCard_AutoNumbersSequentially()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Act
        var numbers = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/cards", new
            {
                name = $"Sequential Card {i}",
                descriptionMarkdown = "",
                status = "Open",
                size = "S",
                laneId,
                position = NextPosition()
            });
            response.EnsureSuccessStatusCode();
            var card = await response.Content.ReadFromJsonAsync<JsonElement>();
            numbers.Add(card.GetProperty("number").GetInt64());
        }

        // Assert
        Assert.Equal(numbers[0] + 1, numbers[1]);
        Assert.Equal(numbers[1] + 1, numbers[2]);
    }

    [Fact]
    public async Task PostCard_AsAgentUser_Returns201()
    {
        // Arrange
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "AgentCardCreator", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, agent.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "Agent Created Card",
            descriptionMarkdown = "Created by agent",
            status = "Open",
            size = "M",
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/cards", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PatchCard_UpdatesName_Returns200WithUpdatedTimestamp()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Original Name",
            descriptionMarkdown = "desc",
            status = "Open",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();
        var originalTimestamp = created.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset();

        await Task.Delay(50);

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { name = "Updated Name" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Name", updated.GetProperty("name").GetString());
        Assert.True(updated.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset() > originalTimestamp);
    }

    [Fact]
    public async Task PatchCard_MovesToAnotherLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var sourceLaneId = await GetLaneIdByIndexAsync(0);
        var targetLaneId = await GetLaneIdByIndexAsync(1);
        var pos = NextPosition();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Movable Card",
            descriptionMarkdown = "will move",
            status = "Open",
            size = "M",
            laneId = sourceLaneId,
            position = pos
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act — move to target lane, keeping same position (unique in the new lane)
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { laneId = targetLaneId });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(targetLaneId, updated.GetProperty("laneId").GetGuid());
    }

    [Fact]
    public async Task PatchCard_NonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var bogusId = Guid.NewGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{bogusId}", new { name = "Ghost" });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchCard_PartialUpdate_PreservesUnchangedFields()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();
        var pos = NextPosition();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Stable Card",
            descriptionMarkdown = "Original description",
            status = "Open",
            size = "M",
            laneId,
            position = pos
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();
        var originalDescription = created.GetProperty("descriptionMarkdown").GetString();

        // Act -- only update name
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { name = "Renamed Card" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Renamed Card", updated.GetProperty("name").GetString());
        Assert.Equal(originalDescription, updated.GetProperty("descriptionMarkdown").GetString());
        Assert.Equal("M", updated.GetProperty("size").GetString());
        Assert.Equal(laneId, updated.GetProperty("laneId").GetGuid());
        Assert.Equal(pos, updated.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task DeleteCard_AsAdmin_Returns204()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Admin Delete Target",
            descriptionMarkdown = "",
            status = "Open",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/cards/{cardId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCard_AsHumanUser_Returns204()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanDeleter", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Human Delete Target",
            descriptionMarkdown = "",
            status = "Open",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/cards/{cardId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCard_AsAgentUser_Returns403()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Agent Cannot Delete This",
            descriptionMarkdown = "",
            status = "Open",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "AgentNoDelete", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, agent.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/cards/{cardId}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCard_NonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var bogusId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/cards/{bogusId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

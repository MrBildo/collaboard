using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class LaneEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;

    public LaneEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostLane_AsAdmin_Returns201WithCorrectFields()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var request = new { name = "Review", position = 3 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/lanes", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var lane = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Review", lane.GetProperty("name").GetString());
        Assert.Equal(3, lane.GetProperty("position").GetInt32());
        Assert.NotEqual(Guid.Empty, lane.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task PostLane_AsHumanUser_Returns403()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanLaneTester", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, user.AuthKey);
        var request = new { name = "Blocked", position = 4 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/lanes", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLane_EmptyLane_Returns204()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var createResponse = await _client.PostAsJsonAsync("/api/v1/lanes", new { name = "Disposable", position = 99 });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var laneId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{laneId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLane_LaneWithCards_Returns409()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var laneResponse = await _client.PostAsJsonAsync("/api/v1/lanes", new { name = "HasCards", position = 100 });
        laneResponse.EnsureSuccessStatusCode();
        var lane = await laneResponse.Content.ReadFromJsonAsync<JsonElement>();
        var laneId = lane.GetProperty("id").GetGuid();

        var cardResponse = await _client.PostAsJsonAsync("/api/v1/cards", new
        {
            name = "Blocker Card",
            descriptionMarkdown = "Prevents deletion",
            laneId,
            position = 0,
            status = "Open",
            size = "M"
        });
        cardResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{laneId}");

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLane_NonexistentLane_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var bogusId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{bogusId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteLane_AsHumanUser_Returns403()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanDeleter", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, user.AuthKey);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstLaneId = board.GetProperty("lanes")[0].GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{firstLaneId}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

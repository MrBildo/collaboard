using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class LaneEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostLane_AsAdmin_Returns201WithCorrectFields()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var request = new { name = "Review", position = 3 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/lanes", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var lane = await response.Content.ReadFromJsonAsync<JsonElement>();
        lane.GetProperty("name").GetString().ShouldBe("Review");
        lane.GetProperty("position").GetInt32().ShouldBe(3);
        lane.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task PostLane_AsHumanUser_Returns403()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanLaneTester", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, user.AuthKey);
        var request = new { name = "Blocked", position = 4 };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/lanes", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
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
            size = "M"
        });
        cardResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{laneId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
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
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteLane_AsHumanUser_Returns403()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanDeleter", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, user.AuthKey);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstLaneId = board.GetProperty("lanes")[0].GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/lanes/{firstLaneId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class BoardEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task GetBoard_AsAdmin_Returns200WithLanesAndEmptyCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        json.TryGetProperty("lanes", out var lanes).ShouldBeTrue();
        lanes.GetArrayLength().ShouldBe(3);
        json.TryGetProperty("cards", out var cards).ShouldBeTrue();
        cards.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetBoard_AsHumanUser_Returns200()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Board Human", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoard_AsAgentUser_Returns200()
    {
        // Arrange
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "Board Agent", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, agent.AuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBoard_Unauthenticated_Returns401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Remove("X-User-Key");

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBoard_LanesAreOrderedByPosition()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var lanes = json.GetProperty("lanes");
        lanes.GetArrayLength().ShouldBe(3);

        var lane0 = lanes[0];
        var lane1 = lanes[1];
        var lane2 = lanes[2];

        lane0.GetProperty("position").GetInt32().ShouldBe(0);
        lane0.GetProperty("name").GetString().ShouldBe("Backlog");
        lane1.GetProperty("position").GetInt32().ShouldBe(1);
        lane1.GetProperty("name").GetString().ShouldBe("In Progress");
        lane2.GetProperty("position").GetInt32().ShouldBe(2);
        lane2.GetProperty("name").GetString().ShouldBe("Done");
    }

    [Fact]
    public async Task GetBoard_AfterCreatingCard_CardAppearsInResponse()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        var boardJson = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var firstLaneId = boardJson.GetProperty("lanes")[0].GetProperty("id").GetGuid();

        var cardPayload = new
        {
            name = "Test Card",
            descriptionMarkdown = "A card for testing",
            laneId = firstLaneId,
            position = 0,
            status = "Open",
            size = "M",
        };

        // Act
        var createResponse = await _client.PostAsJsonAsync("/api/v1/cards", cardPayload);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var refreshedBoard = await _client.GetAsync("/api/v1/board");

        // Assert
        refreshedBoard.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshedJson = await refreshedBoard.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var cards = refreshedJson.GetProperty("cards");
        cards.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        var found = false;
        foreach (var card in cards.EnumerateArray())
        {
            if (card.GetProperty("name").GetString() == "Test Card")
            {
                card.GetProperty("descriptionMarkdown").GetString().ShouldBe("A card for testing");
                card.GetProperty("laneId").GetGuid().ShouldBe(firstLaneId);
                found = true;
                break;
            }
        }

        found.ShouldBeTrue("Created card was not found in the board response.");
    }
}

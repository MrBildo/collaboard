using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class BoardEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BoardEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBoard_AsAdmin_Returns200WithLanesAndEmptyCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.TryGetProperty("lanes", out var lanes));
        Assert.Equal(3, lanes.GetArrayLength());
        Assert.True(json.TryGetProperty("cards", out var cards));
        Assert.Equal(JsonValueKind.Array, cards.ValueKind);
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBoard_LanesAreOrderedByPosition()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/board");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var lanes = json.GetProperty("lanes");
        Assert.Equal(3, lanes.GetArrayLength());

        var lane0 = lanes[0];
        var lane1 = lanes[1];
        var lane2 = lanes[2];

        Assert.Equal(0, lane0.GetProperty("position").GetInt32());
        Assert.Equal("Backlog", lane0.GetProperty("name").GetString());
        Assert.Equal(1, lane1.GetProperty("position").GetInt32());
        Assert.Equal("In Progress", lane1.GetProperty("name").GetString());
        Assert.Equal(2, lane2.GetProperty("position").GetInt32());
        Assert.Equal("Done", lane2.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetBoard_AfterCreatingCard_CardAppearsInResponse()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        var boardJson = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
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
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var refreshedBoard = await _client.GetAsync("/api/v1/board");

        // Assert
        Assert.Equal(HttpStatusCode.OK, refreshedBoard.StatusCode);

        var refreshedJson = await refreshedBoard.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var cards = refreshedJson.GetProperty("cards");
        Assert.True(cards.GetArrayLength() >= 1);

        var found = false;
        foreach (var card in cards.EnumerateArray())
        {
            if (card.GetProperty("name").GetString() == "Test Card")
            {
                Assert.Equal("A card for testing", card.GetProperty("descriptionMarkdown").GetString());
                Assert.Equal(firstLaneId, card.GetProperty("laneId").GetGuid());
                found = true;
                break;
            }
        }
        Assert.True(found, "Created card was not found in the board response.");
    }
}

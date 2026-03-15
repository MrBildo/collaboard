using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class CardEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private static int _nextPosition = 1000;

    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static int NextPosition() => Interlocked.Increment(ref _nextPosition);

    private async Task<Guid> GetFirstLaneIdAsync()
    {
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/board");
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("lanes")[0].GetProperty("id").GetGuid();
    }

    private async Task<Guid> GetLaneIdByIndexAsync(int index)
    {
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/board");
        response.EnsureSuccessStatusCode();
        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        return board.GetProperty("lanes")[index].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task GetCards_ReturnsAllCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Create a card to ensure at least one exists
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "GetCards Test Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        cards.ShouldNotBeNull();
        cards.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetCardById_Returns200()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "GetById Card",
            descriptionMarkdown = "Find me",
            size = "S",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("id").GetGuid().ShouldBe(cardId);
        card.GetProperty("name").GetString().ShouldBe("GetById Card");
    }

    [Fact]
    public async Task GetCardById_NonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var bogusId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{bogusId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCard_AsHumanUser_Returns201WithAutoNumberAndTimestamps()
    {
        // Arrange
        var user = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanCardCreator", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, user.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "My First Card",
            descriptionMarkdown = "Some description",
            size = "L",
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("number").GetInt64().ShouldBeGreaterThan(0);
        card.GetProperty("createdByUserId").GetGuid().ShouldBe(user.Id);
        card.GetProperty("createdAtUtc").GetDateTimeOffset().ShouldNotBe(default);
        card.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset().ShouldNotBe(default);
    }

    [Fact]
    public async Task PostCard_AutoNumbersSequentially()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Act
        List<long> numbers = [];
        for (var i = 0; i < 3; i++)
        {
            var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
            {
                name = $"Sequential Card {i}",
                descriptionMarkdown = "",
                size = "S",
                laneId,
                position = NextPosition()
            });
            response.EnsureSuccessStatusCode();
            var card = await response.Content.ReadFromJsonAsync<JsonElement>();
            numbers.Add(card.GetProperty("number").GetInt64());
        }

        // Assert
        numbers[1].ShouldBe(numbers[0] + 1);
        numbers[2].ShouldBe(numbers[1] + 1);
    }

    [Fact]
    public async Task PostCard_AsAgentUser_Returns201()
    {
        // Arrange
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "AgentCardCreator", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, agent.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "Agent Created Card",
            descriptionMarkdown = "Created by agent",
            size = "M",
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PatchCard_UpdatesName_Returns200WithUpdatedTimestamp()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Original Name",
            descriptionMarkdown = "desc",
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().ShouldBe("Updated Name");
        updated.GetProperty("lastUpdatedAtUtc").GetDateTimeOffset().ShouldBeGreaterThan(originalTimestamp);
    }

    [Fact]
    public async Task PatchCard_MovesToAnotherLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var sourceLaneId = await GetLaneIdByIndexAsync(0);
        var targetLaneId = await GetLaneIdByIndexAsync(1);
        var pos = NextPosition();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Movable Card",
            descriptionMarkdown = "will move",
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("laneId").GetGuid().ShouldBe(targetLaneId);
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
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PatchCard_PartialUpdate_PreservesUnchangedFields()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();
        var pos = NextPosition();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Stable Card",
            descriptionMarkdown = "Original description",
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().ShouldBe("Renamed Card");
        updated.GetProperty("descriptionMarkdown").GetString().ShouldBe(originalDescription);
        updated.GetProperty("size").GetString().ShouldBe("M");
        updated.GetProperty("laneId").GetGuid().ShouldBe(laneId);
        updated.GetProperty("position").GetInt32().ShouldBe(pos);
    }

    [Fact]
    public async Task DeleteCard_AsAdmin_Returns204()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Admin Delete Target",
            descriptionMarkdown = "",
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
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCard_AsHumanUser_Returns204()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "HumanDeleter", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, human.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Human Delete Target",
            descriptionMarkdown = "",
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
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCard_AsAgentUser_Returns403()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Agent Cannot Delete This",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "AgentNoDelete", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, agent.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/cards/{cardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCard_InvalidSize_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "Invalid Size Card",
            descriptionMarkdown = "",
            size = "XXL",
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("S")]
    [InlineData("M")]
    [InlineData("L")]
    [InlineData("XL")]
    public async Task PostCard_ValidSizes_AllAccepted(string size)
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = $"Card Size {size}",
            descriptionMarkdown = "",
            size,
            laneId,
            position = NextPosition()
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("size").GetString().ShouldBe(size);
    }

    [Fact]
    public async Task PatchCard_SetPositionToZero_Works()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Position Zero Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { position = 0 });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("position").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task PatchCard_InvalidSize_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card For Invalid Size Patch",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { size = "XXL" });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReorderCard_SameLane_MovesToNewIndex()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var cardIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
            {
                name = $"Reorder Same Lane Card {i}",
                descriptionMarkdown = "",
                size = "M",
                laneId,
                position = i * 10
            });
            createResponse.EnsureSuccessStatusCode();
            var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            cardIds.Add(created.GetProperty("id").GetGuid());
        }

        // Act — move last card (index 2) to index 0
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardIds[2]}/reorder", new
        {
            laneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cards = board.GetProperty("cards");
        var reorderedCards = new List<JsonElement>();
        foreach (var c in cards.EnumerateArray())
        {
            if (c.GetProperty("laneId").GetGuid() == laneId && cardIds.Contains(c.GetProperty("id").GetGuid()))
            {
                reorderedCards.Add(c);
            }
        }

        reorderedCards.Count.ShouldBe(3);
        reorderedCards[0].GetProperty("id").GetGuid().ShouldBe(cardIds[2]);
        reorderedCards[0].GetProperty("position").GetInt32().ShouldBeLessThan(reorderedCards[1].GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task ReorderCard_CrossLane_MovesToTargetLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var sourceLaneId = await GetLaneIdByIndexAsync(0);
        var targetLaneId = await GetLaneIdByIndexAsync(1);

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Cross Lane Reorder Card",
            descriptionMarkdown = "",
            size = "M",
            laneId = sourceLaneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act — move to target lane at index 0
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId = targetLaneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cards = board.GetProperty("cards");
        JsonElement? movedCard = null;
        foreach (var c in cards.EnumerateArray())
        {
            if (c.GetProperty("id").GetGuid() == cardId)
            {
                movedCard = c;
                break;
            }
        }

        movedCard.ShouldNotBeNull();
        movedCard.Value.GetProperty("laneId").GetGuid().ShouldBe(targetLaneId);
    }

    [Fact]
    public async Task ReorderCard_ToEmptyLane_Works()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var sourceLaneId = await GetFirstLaneIdAsync();

        // Create a new empty lane
        var laneResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/lanes", new
        {
            name = $"Empty Lane {Guid.NewGuid()}",
            position = NextPosition()
        });
        laneResponse.EnsureSuccessStatusCode();
        var lane = await laneResponse.Content.ReadFromJsonAsync<JsonElement>();
        var emptyLaneId = lane.GetProperty("id").GetGuid();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card To Empty Lane",
            descriptionMarkdown = "",
            size = "M",
            laneId = sourceLaneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act — move to empty lane at index 0
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId = emptyLaneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cards = board.GetProperty("cards");
        JsonElement? movedCard = null;
        foreach (var c in cards.EnumerateArray())
        {
            if (c.GetProperty("id").GetGuid() == cardId)
            {
                movedCard = c;
                break;
            }
        }

        movedCard.ShouldNotBeNull();
        movedCard.Value.GetProperty("laneId").GetGuid().ShouldBe(emptyLaneId);
        movedCard.Value.GetProperty("position").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task ReorderCard_NonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();
        var bogusId = Guid.NewGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{bogusId}/reorder", new
        {
            laneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReorderCard_NonexistentLane_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card For Bad Lane Reorder",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId = Guid.NewGuid(),
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReorderCard_ReturnsFullBoard()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Full Board Reorder Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var board = await response.Content.ReadFromJsonAsync<JsonElement>();
        board.TryGetProperty("lanes", out var lanes).ShouldBeTrue();
        board.TryGetProperty("cards", out var cards).ShouldBeTrue();
        lanes.GetArrayLength().ShouldBeGreaterThan(0);
        cards.GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task PatchCard_WithLabelIds_ReplacesLabels()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Create two labels
        var label1Response = await _client.PostAsJsonAsync("/api/v1/labels", new { name = $"CardLabel1-{Guid.NewGuid()}", color = "red" });
        label1Response.EnsureSuccessStatusCode();
        var label1 = await label1Response.Content.ReadFromJsonAsync<JsonElement>();
        var label1Id = label1.GetProperty("id").GetGuid();

        var label2Response = await _client.PostAsJsonAsync("/api/v1/labels", new { name = $"CardLabel2-{Guid.NewGuid()}", color = "blue" });
        label2Response.EnsureSuccessStatusCode();
        var label2 = await label2Response.Content.ReadFromJsonAsync<JsonElement>();
        var label2Id = label2.GetProperty("id").GetGuid();

        // Create a card
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Labeled Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = NextPosition()
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Assign label1 via patch
        var patchResponse1 = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { labelIds = new[] { label1Id } });
        patchResponse1.EnsureSuccessStatusCode();

        // Act — replace with label2 only
        var patchResponse2 = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { labelIds = new[] { label2Id } });

        // Assert
        patchResponse2.StatusCode.ShouldBe(HttpStatusCode.OK);

        var labelsResponse = await _client.GetAsync($"/api/v1/cards/{cardId}/labels");
        labelsResponse.EnsureSuccessStatusCode();
        var labels = await labelsResponse.Content.ReadFromJsonAsync<JsonElement>();
        labels.GetArrayLength().ShouldBe(1);
        labels[0].GetProperty("id").GetGuid().ShouldBe(label2Id);
    }
}

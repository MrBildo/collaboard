using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Collaboard.Api.Tests;

public class CardEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> GetFirstLaneIdAsync()
        => await TestDataHelper.GetFirstLaneIdAsync(_client, _factory.DefaultBoardId);

    private async Task<Guid> GetLaneIdByIndexAsync(int index)
        => await TestDataHelper.GetLaneIdByIndexAsync(_client, _factory.DefaultBoardId, index);

    private async Task<Guid> GetSizeIdByNameAsync(string sizeName)
        => await TestDataHelper.GetSizeIdByNameAsync(_client, _factory.DefaultBoardId, sizeName);

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
            position = Random.Shared.Next(10000, 99999)
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
    public async Task GetCardById_ReturnsEnrichedResponse()
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
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Add a comment
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Test comment" });

        // Add an attachment
        using var attachContent = new MultipartFormDataContent();
        attachContent.Add(new ByteArrayContent([1, 2, 3]), "file", "test.txt");
        await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", attachContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("card").GetProperty("id").GetGuid().ShouldBe(cardId);
        body.GetProperty("card").GetProperty("name").GetString().ShouldBe("GetById Card");

        // User display names
        body.GetProperty("createdByUserName").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("lastUpdatedByUserName").GetString().ShouldNotBeNullOrEmpty();

        // Comments with user names
        var comments = body.GetProperty("comments");
        comments.GetArrayLength().ShouldBeGreaterThan(0);
        comments[0].TryGetProperty("userName", out _).ShouldBeTrue();

        // Labels array present
        body.TryGetProperty("labels", out _).ShouldBeTrue();

        // Attachments
        var attachments = body.GetProperty("attachments");
        attachments.GetArrayLength().ShouldBeGreaterThan(0);
        attachments[0].GetProperty("fileName").GetString().ShouldBe("test.txt");
        attachments[0].TryGetProperty("payload", out _).ShouldBeFalse();
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
            position = Random.Shared.Next(10000, 99999)
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
                position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
        var pos = Random.Shared.Next(10000, 99999);

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
        var pos = Random.Shared.Next(10000, 99999);

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
        updated.GetProperty("sizeId").GetGuid().ShouldNotBe(Guid.Empty);
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
    public async Task PostCard_InvalidSizeId_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var request = new
        {
            name = "Invalid Size Card",
            descriptionMarkdown = "",
            sizeId = Guid.NewGuid(),
            laneId,
            position = Random.Shared.Next(10000, 99999)
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
        var sizeId = await GetSizeIdByNameAsync(size);

        var request = new
        {
            name = $"Card Size {size}",
            descriptionMarkdown = "",
            sizeId,
            laneId,
            position = Random.Shared.Next(10000, 99999)
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("sizeId").GetGuid().ShouldBe(sizeId);
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
            position = Random.Shared.Next(10000, 99999)
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
    public async Task PatchCard_InvalidSizeId_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card For Invalid Size Patch",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { sizeId = Guid.NewGuid() });

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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
            position = Random.Shared.Next(10000, 99999)
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
    public async Task ReorderCard_CrossBoard_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createCardResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Cross Board Reorder Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createCardResponse.EnsureSuccessStatusCode();
        var card = await createCardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = card.GetProperty("id").GetGuid();

        // Create a second board with a lane
        var boardResponse = await _client.PostAsJsonAsync("/api/v1/boards", new { name = $"OtherBoard-{Guid.NewGuid()}" });
        boardResponse.EnsureSuccessStatusCode();
        var otherBoard = await boardResponse.Content.ReadFromJsonAsync<JsonElement>();
        var otherBoardId = otherBoard.GetProperty("id").GetGuid();

        var otherLaneResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{otherBoardId}/lanes", new { name = "Other Lane", position = 0 });
        otherLaneResponse.EnsureSuccessStatusCode();
        var otherLane = await otherLaneResponse.Content.ReadFromJsonAsync<JsonElement>();
        var otherLaneId = otherLane.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId = otherLaneId,
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCard_NewlinesInDescription_PreservedOnRoundTrip()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();
        var description = "Line one\nLine two\nLine three";

        var request = new
        {
            name = "Newline Card",
            descriptionMarkdown = description,
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("descriptionMarkdown").GetString().ShouldBe(description);
    }

    [Fact]
    public async Task PatchCard_NewlinesInDescription_PreservedOnRoundTrip()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card For Newline Patch",
            descriptionMarkdown = "initial",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        var newDescription = "Updated line one\nUpdated line two\ttab here";

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new { descriptionMarkdown = newDescription });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("descriptionMarkdown").GetString().ShouldBe(newDescription);
    }

    [Fact]
    public async Task GetCards_IncludesLabelsAndCounts()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Create a label
        var labelResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"EnrichLabel-{Guid.NewGuid()}", color = "green" });
        labelResponse.EnsureSuccessStatusCode();
        var label = await labelResponse.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetGuid();

        // Create a card
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Enriched Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Assign label to card
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/labels", new { labelId });

        // Add a comment
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Test comment" });

        // Add an attachment
        var attachContent = new MultipartFormDataContent();
        attachContent.Add(new ByteArrayContent([1, 2, 3]), "file", "test.txt");
        await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", attachContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        cards.ShouldNotBeNull();

        var enrichedCard = cards.First(c => c.GetProperty("id").GetGuid() == cardId);
        enrichedCard.GetProperty("labels").GetArrayLength().ShouldBe(1);
        enrichedCard.GetProperty("labels")[0].GetProperty("id").GetGuid().ShouldBe(labelId);
        enrichedCard.GetProperty("labels")[0].GetProperty("name").GetString()!.ShouldContain("EnrichLabel");
        enrichedCard.GetProperty("labels")[0].GetProperty("color").GetString().ShouldBe("green");
        enrichedCard.GetProperty("commentCount").GetInt32().ShouldBe(1);
        enrichedCard.GetProperty("attachmentCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GetCards_CardWithNoLabelsOrCounts_ReturnsEmptyAndZero()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Bare Card No Extras",
            descriptionMarkdown = "",
            size = "S",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        cards.ShouldNotBeNull();

        var bareCard = cards.First(c => c.GetProperty("id").GetGuid() == cardId);
        bareCard.GetProperty("labels").GetArrayLength().ShouldBe(0);
        bareCard.GetProperty("commentCount").GetInt32().ShouldBe(0);
        bareCard.GetProperty("attachmentCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task PatchCard_WithLabelIds_ReplacesLabels()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Create two labels (board-scoped)
        var label1Response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels", new { name = $"CardLabel1-{Guid.NewGuid()}", color = "red" });
        label1Response.EnsureSuccessStatusCode();
        var label1 = await label1Response.Content.ReadFromJsonAsync<JsonElement>();
        var label1Id = label1.GetProperty("id").GetGuid();

        var label2Response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels", new { name = $"CardLabel2-{Guid.NewGuid()}", color = "blue" });
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
            position = Random.Shared.Next(10000, 99999)
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

    [Fact]
    public async Task GetCardById_ReturnsAttachmentsWithoutPayload()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card With Attachment",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        using var attachContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        attachContent.Add(fileContent, "file", "image.png");
        var attachResponse = await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", attachContent);
        attachResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var attachments = body.GetProperty("attachments");
        attachments.GetArrayLength().ShouldBe(1);

        var attachment = attachments[0];
        attachment.GetProperty("fileName").GetString().ShouldBe("image.png");
        attachment.GetProperty("contentType").GetString().ShouldBe("image/png");
        attachment.GetProperty("addedByUserId").GetGuid().ShouldNotBe(Guid.Empty);
        attachment.TryGetProperty("payload", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GetCardById_ReturnsUserDisplayNames()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "DisplayNameUser", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, human.AuthKey);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card With User Names",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Add a comment
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Hello" });

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("createdByUserName").GetString().ShouldBe("DisplayNameUser");
        body.GetProperty("lastUpdatedByUserName").GetString().ShouldBe("DisplayNameUser");

        var comments = body.GetProperty("comments");
        comments.GetArrayLength().ShouldBe(1);
        comments[0].GetProperty("userName").GetString().ShouldBe("DisplayNameUser");
    }

    [Fact]
    public async Task McpGetCard_ByCardNumber_ReturnsCard()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Card Number Lookup",
            descriptionMarkdown = "Find by number",
            size = "S",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardNumber = created.GetProperty("number").GetInt64();

        // Act
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<Mcp.McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<Events.BoardEventBroadcaster>();
        var cardTools = new Mcp.CardTools(db, authService, broadcaster);

        var result = await cardTools.GetCardAsync(_factory.AdminAuthKey, cardNumber: cardNumber, boardId: _factory.DefaultBoardId);

        // Assert
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("card").GetProperty("name").GetString().ShouldBe("Card Number Lookup");
        parsed.GetProperty("card").GetProperty("number").GetInt64().ShouldBe(cardNumber);
    }

    [Fact]
    public async Task McpGetCard_NeitherIdNorNumber_ReturnsError()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<Mcp.McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<Events.BoardEventBroadcaster>();
        var cardTools = new Mcp.CardTools(db, authService, broadcaster);

        // Act
        var result = await cardTools.GetCardAsync(_factory.AdminAuthKey);

        // Assert
        result.ShouldContain("Provide either cardId or cardNumber");
    }

    [Fact]
    public async Task McpGetCard_InvalidCardNumber_ReturnsError()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<Mcp.McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<Events.BoardEventBroadcaster>();
        var cardTools = new Mcp.CardTools(db, authService, broadcaster);

        // Act
        var result = await cardTools.GetCardAsync(_factory.AdminAuthKey, cardNumber: 999999, boardId: _factory.DefaultBoardId);

        // Assert
        result.ShouldContain("not found");
    }

    [Fact]
    public async Task McpGetCard_IncludesAttachmentsAndUserNames()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "MCP Enriched Card",
            descriptionMarkdown = "",
            size = "M",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Add a comment
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "MCP test comment" });

        // Add an attachment via REST
        using var attachContent = new MultipartFormDataContent();
        attachContent.Add(new ByteArrayContent([1, 2, 3]), "file", "data.csv");
        await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", attachContent);

        // Act
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<Mcp.McpAuthService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<Events.BoardEventBroadcaster>();
        var cardTools = new Mcp.CardTools(db, authService, broadcaster);

        var result = await cardTools.GetCardAsync(_factory.AdminAuthKey, cardId: cardId);

        // Assert
        var parsed = JsonSerializer.Deserialize<JsonElement>(result);
        parsed.GetProperty("card").GetProperty("name").GetString().ShouldBe("MCP Enriched Card");

        // User names
        parsed.GetProperty("createdByUserName").GetString().ShouldNotBeNullOrEmpty();
        parsed.GetProperty("lastUpdatedByUserName").GetString().ShouldNotBeNullOrEmpty();

        // Comments with user names
        var comments = parsed.GetProperty("comments");
        comments.GetArrayLength().ShouldBe(1);
        comments[0].GetProperty("userName").GetString().ShouldNotBeNullOrEmpty();

        // Attachments (metadata only)
        var attachments = parsed.GetProperty("attachments");
        attachments.GetArrayLength().ShouldBe(1);
        attachments[0].GetProperty("fileName").GetString().ShouldBe("data.csv");
    }

    // ── Hardened enrichment tests (query optimization) ───────────────────────

    [Fact]
    public async Task GetCards_ReturnsCorrectSizeName()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();
        var sizeId = await GetSizeIdByNameAsync("L");

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"SizeName-Test-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999),
            sizeId
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");
        response.EnsureSuccessStatusCode();
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        var card = cards!.First(c => c.GetProperty("id").GetGuid() == cardId);
        card.GetProperty("sizeName").GetString().ShouldBe("L");
        card.GetProperty("sizeId").GetGuid().ShouldBe(sizeId);
    }

    [Fact]
    public async Task GetCards_ReturnsCorrectLabelSummaries()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var label1Response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"QOpt1-{Guid.NewGuid()}", color = "red" });
        label1Response.EnsureSuccessStatusCode();
        var label1 = await label1Response.Content.ReadFromJsonAsync<JsonElement>();
        var label1Id = label1.GetProperty("id").GetGuid();
        var label1Name = label1.GetProperty("name").GetString();

        var label2Response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"QOpt2-{Guid.NewGuid()}", color = "blue" });
        label2Response.EnsureSuccessStatusCode();
        var label2 = await label2Response.Content.ReadFromJsonAsync<JsonElement>();
        var label2Id = label2.GetProperty("id").GetGuid();
        var label2Name = label2.GetProperty("name").GetString();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"LabelSummary-Test-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/labels", new { labelId = label1Id });
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/labels", new { labelId = label2Id });

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");
        response.EnsureSuccessStatusCode();
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        var card = cards!.First(c => c.GetProperty("id").GetGuid() == cardId);
        var labels = card.GetProperty("labels");
        labels.GetArrayLength().ShouldBe(2);

        var labelIds = labels.EnumerateArray().Select(l => l.GetProperty("id").GetGuid()).ToList();
        labelIds.ShouldContain(label1Id);
        labelIds.ShouldContain(label2Id);

        var labelNames = labels.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToList();
        labelNames.ShouldContain(label1Name);
        labelNames.ShouldContain(label2Name);

        var labelColors = labels.EnumerateArray().Select(l => l.GetProperty("color").GetString()).ToList();
        labelColors.ShouldContain("red");
        labelColors.ShouldContain("blue");
    }

    [Fact]
    public async Task GetCards_ReturnsCorrectCommentCount()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"CommentCount-Test-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Comment 1" });
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Comment 2" });

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");
        response.EnsureSuccessStatusCode();
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        var card = cards!.First(c => c.GetProperty("id").GetGuid() == cardId);
        card.GetProperty("commentCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task GetCards_ReturnsCorrectAttachmentCount()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"AttachCount-Test-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        using var attachContent = new MultipartFormDataContent();
        attachContent.Add(new ByteArrayContent([1, 2, 3]), "file", "file1.txt");
        await _client.PostAsync($"/api/v1/cards/{cardId}/attachments", attachContent);

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");
        response.EnsureSuccessStatusCode();
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        var card = cards!.First(c => c.GetProperty("id").GetGuid() == cardId);
        card.GetProperty("attachmentCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task GetCard_CommentsAreOrderedByDate()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"CommentOrder-Test-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Add 3 comments with slight delays so timestamps are distinct
        var c1 = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "First comment" });
        c1.EnsureSuccessStatusCode();
        await Task.Delay(50);

        var c2 = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Second comment" });
        c2.EnsureSuccessStatusCode();
        await Task.Delay(50);

        var c3 = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Third comment" });
        c3.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        var comments = body.GetProperty("comments");
        comments.GetArrayLength().ShouldBe(3);

        var timestamps = comments.EnumerateArray()
            .Select(c => DateTimeOffset.Parse(c.GetProperty("lastUpdatedAtUtc").GetString()!))
            .ToList();

        // Comments should be in chronological order
        for (var i = 1; i < timestamps.Count; i++)
        {
            timestamps[i].ShouldBeGreaterThanOrEqualTo(timestamps[i - 1]);
        }

        // Content order should match creation order
        comments[0].GetProperty("contentMarkdown").GetString().ShouldBe("First comment");
        comments[1].GetProperty("contentMarkdown").GetString().ShouldBe("Second comment");
        comments[2].GetProperty("contentMarkdown").GetString().ShouldBe("Third comment");
    }

    [Fact]
    public async Task GetComments_ReturnsOrderedByDate()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"CommentEndpointOrder-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Alpha" });
        await Task.Delay(50);
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Beta" });
        await Task.Delay(50);
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", new { contentMarkdown = "Gamma" });

        // Act
        var response = await _client.GetAsync($"/api/v1/cards/{cardId}/comments");
        response.EnsureSuccessStatusCode();
        var comments = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        comments!.Length.ShouldBe(3);
        comments[0].GetProperty("contentMarkdown").GetString().ShouldBe("Alpha");
        comments[1].GetProperty("contentMarkdown").GetString().ShouldBe("Beta");
        comments[2].GetProperty("contentMarkdown").GetString().ShouldBe("Gamma");

        var timestamps = comments.Select(c => DateTimeOffset.Parse(c.GetProperty("lastUpdatedAtUtc").GetString()!)).ToList();
        for (var i = 1; i < timestamps.Count; i++)
        {
            timestamps[i].ShouldBeGreaterThanOrEqualTo(timestamps[i - 1]);
        }
    }

    [Fact]
    public async Task GetCards_MultipleCardsWithDifferentEnrichment_AllCorrect()
    {
        // Arrange — create two cards: one with labels+comments, one bare
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var labelResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/labels",
            new { name = $"MultiCard-{Guid.NewGuid()}", color = "teal" });
        labelResponse.EnsureSuccessStatusCode();
        var labelId = (await labelResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Card A: with label and 2 comments
        var cardAResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"MultiA-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        cardAResponse.EnsureSuccessStatusCode();
        var cardAId = (await cardAResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardAId}/labels", new { labelId });
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardAId}/comments", new { contentMarkdown = "C1" });
        await _client.PostAsJsonAsync($"/api/v1/cards/{cardAId}/comments", new { contentMarkdown = "C2" });

        // Card B: bare (no labels, no comments, no attachments)
        var cardBResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = $"MultiB-{Guid.NewGuid()}",
            descriptionMarkdown = "",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        cardBResponse.EnsureSuccessStatusCode();
        var cardBId = (await cardBResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards");
        response.EnsureSuccessStatusCode();
        var cards = await response.Content.ReadFromJsonAsync<JsonElement[]>();

        // Assert
        var cardA = cards!.First(c => c.GetProperty("id").GetGuid() == cardAId);
        cardA.GetProperty("labels").GetArrayLength().ShouldBe(1);
        cardA.GetProperty("commentCount").GetInt32().ShouldBe(2);
        cardA.GetProperty("attachmentCount").GetInt32().ShouldBe(0);
        cardA.GetProperty("sizeName").GetString().ShouldNotBe("?");

        var cardB = cards!.First(c => c.GetProperty("id").GetGuid() == cardBId);
        cardB.GetProperty("labels").GetArrayLength().ShouldBe(0);
        cardB.GetProperty("commentCount").GetInt32().ShouldBe(0);
        cardB.GetProperty("attachmentCount").GetInt32().ShouldBe(0);
        cardB.GetProperty("sizeName").GetString().ShouldNotBe("?");
    }

    // ── Validation tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task PostCard_WithoutLaneId_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "No Lane Card"
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCard_WithoutName_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCard_WithEmptyName_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "   ",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReorder_WithoutLaneId_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Reorder No LaneId",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            index = 0
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostReorder_WithoutIndex_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Reorder No Index",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/reorder", new
        {
            laneId
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchCard_WithNonexistentLaneId_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Patch Bad Lane",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new
        {
            laneId = Guid.NewGuid()
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PatchCard_WithEmptyName_Returns400()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        var createResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Patch Empty Name",
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = created.GetProperty("id").GetGuid();

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/v1/cards/{cardId}", new
        {
            name = "  "
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCard_WithoutPosition_DefaultsToTopOfLane()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Create an existing card with a known position
        var existingResponse = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Existing Card",
            laneId,
            position = 100
        });
        existingResponse.EnsureSuccessStatusCode();

        // Act — create card without specifying position
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Top Card",
            laneId
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("position").GetInt32().ShouldBeLessThan(100);
    }

    [Fact]
    public async Task PostCard_WithExplicitPosition_UsesProvidedValue()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var laneId = await GetFirstLaneIdAsync();

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name = "Explicit Position Card",
            laneId,
            position = 42
        });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("position").GetInt32().ShouldBe(42);
    }
}

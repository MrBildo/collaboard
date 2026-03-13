using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class CommentEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;
    private static int _nextPosition = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public CommentEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<Guid> CreateCardAsync()
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var laneId = board.GetProperty("lanes")[0].GetProperty("id").GetGuid();

        var cardPayload = new
        {
            name = "Test Card",
            descriptionMarkdown = "Card for comment tests",
            laneId,
            position = Interlocked.Increment(ref _nextPosition),
        };

        var cardResponse = await _client.PostAsJsonAsync("/api/v1/cards", cardPayload);
        cardResponse.EnsureSuccessStatusCode();
        var card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return card.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task PostComment_OnExistingCard_Returns201()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var payload = new { contentMarkdown = "This is a test comment." };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.TryGetProperty("id", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());
        Assert.Equal(cardId, json.GetProperty("cardId").GetGuid());
        Assert.NotEqual(Guid.Empty, json.GetProperty("userId").GetGuid());
        Assert.Equal("This is a test comment.", json.GetProperty("contentMarkdown").GetString());
        Assert.True(json.TryGetProperty("lastUpdatedAtUtc", out _));
    }

    [Fact]
    public async Task PostComment_OnNonexistentCard_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fakeCardId = Guid.NewGuid();
        var payload = new { contentMarkdown = "Comment on missing card." };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/v1/cards/{fakeCardId}/comments", payload);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_OwnComment_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Owner", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);

        var payload = new { contentMarkdown = "My comment to delete." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_OtherUsersComment_AsAdmin_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Author", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);

        var payload = new { contentMarkdown = "Someone else's comment." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_OtherUsersComment_AsNonAdmin_Returns403()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var author = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Author 2", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, author.AuthKey);

        var payload = new { contentMarkdown = "Author's comment." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        var otherUser = await TestAuthHelper.CreateUserAsync(_client, _factory, "Other User", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, otherUser.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_NonexistentComment_Returns404()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var fakeCommentId = Guid.NewGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{fakeCommentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

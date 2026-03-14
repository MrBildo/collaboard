using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class CommentEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();
    private static int _nextPosition = 2000;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private async Task<Guid> CreateCardAsync()
    {
        TestAuthHelper.SetAdminAuth(_client, _factory);

        var boardResponse = await _client.GetAsync("/api/v1/board");
        boardResponse.EnsureSuccessStatusCode();
        var board = await boardResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
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
        var card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
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
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        json.TryGetProperty("id", out var idProp).ShouldBeTrue();
        idProp.GetGuid().ShouldNotBe(Guid.Empty);
        json.GetProperty("cardId").GetGuid().ShouldBe(cardId);
        json.GetProperty("userId").GetGuid().ShouldNotBe(Guid.Empty);
        json.GetProperty("contentMarkdown").GetString().ShouldBe("This is a test comment.");
        json.TryGetProperty("lastUpdatedAtUtc", out _).ShouldBeTrue();
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
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteComment_OwnComment_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Owner", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, human.AuthKey);

        var payload = new { contentMarkdown = "My comment to delete." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteComment_OtherUsersComment_AsAdmin_Returns204()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Author", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, human.AuthKey);

        var payload = new { contentMarkdown = "Someone else's comment." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteComment_OtherUsersComment_AsNonAdmin_Returns403()
    {
        // Arrange
        var cardId = await CreateCardAsync();
        var author = await TestAuthHelper.CreateUserAsync(_client, _factory, "Comment Author 2", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, author.AuthKey);

        var payload = new { contentMarkdown = "Author's comment." };
        var createResponse = await _client.PostAsJsonAsync($"/api/v1/cards/{cardId}/comments", payload);
        createResponse.EnsureSuccessStatusCode();
        var comment = await createResponse.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var commentId = comment.GetProperty("id").GetGuid();

        var otherUser = await TestAuthHelper.CreateUserAsync(_client, _factory, "Other User", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, otherUser.AuthKey);

        // Act
        var response = await _client.DeleteAsync($"/api/v1/comments/{commentId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

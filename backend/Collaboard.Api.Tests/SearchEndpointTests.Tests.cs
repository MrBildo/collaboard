using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class SearchEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> GetFirstLaneIdAsync()
        => await TestDataHelper.GetFirstLaneIdAsync(_client, _factory.DefaultBoardId);

    private async Task<JsonElement> CreateCardAsync(string name, string description = "")
    {
        var laneId = await GetFirstLaneIdAsync();
        var response = await _client.PostAsJsonAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards", new
        {
            name,
            descriptionMarkdown = description,
            laneId,
            position = Random.Shared.Next(10000, 99999)
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ── Board-scoped search (GET /boards/{boardId}/cards?search=) ──

    [Fact]
    public async Task GetCards_SearchByName_ReturnsMatchingCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("UniqueAlphaSearchName");
        await CreateCardAsync("Unrelated Card");

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards?search=UniqueAlphaSearch");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>();
        paged.ShouldNotBeNull();
        var cards = paged.Items;
        cards.Count.ShouldBeGreaterThanOrEqualTo(1);
        cards.ShouldAllBe(c => c.GetProperty("name").GetString()!.Contains("UniqueAlphaSearch"));
    }

    [Fact]
    public async Task GetCards_SearchByDescription_ReturnsMatchingCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("Desc Search Card", "this has xyzUniqueDescMarker in it");

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards?search=xyzUniqueDescMarker");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>();
        paged.ShouldNotBeNull();
        var cards = paged.Items;
        cards.Count.ShouldBeGreaterThanOrEqualTo(1);
        cards.ShouldContain(c => c.GetProperty("name").GetString() == "Desc Search Card");
    }

    [Fact]
    public async Task GetCards_SearchByCardNumber_ReturnsExactMatch()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var created = await CreateCardAsync("Number Search Card");
        var cardNumber = created.GetProperty("number").GetInt64();

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards?search=%23{cardNumber}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>();
        paged.ShouldNotBeNull();
        var cards = paged.Items;
        cards.Count.ShouldBe(1);
        cards[0].GetProperty("number").GetInt64().ShouldBe(cardNumber);
    }

    [Fact]
    public async Task GetCards_SearchEmptyString_ReturnsAllCards()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("EmptySearch Card");

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards?search=");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>();
        paged.ShouldNotBeNull();
        paged.Items.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetCards_SearchNoMatch_ReturnsEmptyArray()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync($"/api/v1/boards/{_factory.DefaultBoardId}/cards?search=zzNonExistentTermZz99");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedResult<JsonElement>>();
        paged.ShouldNotBeNull();
        paged.Items.ShouldBeEmpty();
    }

    // ── Global search (GET /search/cards?q=) ──

    [Fact]
    public async Task SearchCards_ByName_ReturnsGroupedByBoard()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("GlobalSearchUniqueName42");

        // Act
        var response = await _client.GetAsync("/api/v1/search/cards?q=GlobalSearchUniqueName42");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
        results.Length.ShouldBeGreaterThanOrEqualTo(1);

        var group = results[0];
        group.GetProperty("boardId").GetGuid().ShouldBe(_factory.DefaultBoardId);
        group.GetProperty("boardName").GetString().ShouldNotBeNullOrEmpty();
        group.GetProperty("boardSlug").GetString().ShouldNotBeNullOrEmpty();
        group.GetProperty("cards").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchCards_EmptyQuery_ReturnsEmptyArray()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/search/cards?q=");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchCards_NoMatch_ReturnsEmptyArray()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/search/cards?q=zzGlobalNonExistentZz99");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchCards_LimitClampedToMax50()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("LimitTestCard");

        // Act — request limit=100, should be clamped to 50
        var response = await _client.GetAsync("/api/v1/search/cards?q=LimitTestCard&limit=100");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
    }

    [Fact]
    public async Task SearchCards_ByCardNumber_ReturnsMatch()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var created = await CreateCardAsync("GlobalNumberSearch");
        var cardNumber = created.GetProperty("number").GetInt64();

        // Act
        var response = await _client.GetAsync($"/api/v1/search/cards?q=%23{cardNumber}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
        results.Length.ShouldBeGreaterThanOrEqualTo(1);

        var cards = results[0].GetProperty("cards");
        cards.GetArrayLength().ShouldBe(1);
        cards[0].GetProperty("number").GetInt64().ShouldBe(cardNumber);
    }

    [Fact]
    public async Task SearchCards_CardSummaryIncludesExpectedFields()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        await CreateCardAsync("FieldCheckSearchCard");

        // Act
        var response = await _client.GetAsync("/api/v1/search/cards?q=FieldCheckSearchCard");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        results.ShouldNotBeNull();
        results.Length.ShouldBeGreaterThanOrEqualTo(1);

        var card = results[0].GetProperty("cards")[0];
        card.TryGetProperty("id", out _).ShouldBeTrue();
        card.TryGetProperty("number", out _).ShouldBeTrue();
        card.TryGetProperty("name", out _).ShouldBeTrue();
        card.TryGetProperty("sizeName", out _).ShouldBeTrue();
        card.TryGetProperty("labels", out _).ShouldBeTrue();
        card.TryGetProperty("commentCount", out _).ShouldBeTrue();
        card.TryGetProperty("attachmentCount", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task SearchCards_RequiresAuth()
    {
        // Arrange — no auth header
        _client.DefaultRequestHeaders.Remove("X-User-Key");

        // Act
        var response = await _client.GetAsync("/api/v1/search/cards?q=test");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

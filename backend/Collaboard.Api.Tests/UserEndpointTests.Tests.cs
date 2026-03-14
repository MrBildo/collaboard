using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class UserEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task PostUser_AsAdmin_Returns201WithUserDetails()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var payload = new { name = "New Human", role = (int)UserRole.HumanUser };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/users", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        json.TryGetProperty("id", out var idProp).ShouldBeTrue();
        idProp.GetGuid().ShouldNotBe(Guid.Empty);
        json.TryGetProperty("authKey", out var authKeyProp).ShouldBeTrue();
        authKeyProp.GetString().ShouldNotBeNullOrWhiteSpace();
        json.GetProperty("name").GetString().ShouldBe("New Human");
        json.GetProperty("role").GetInt32().ShouldBe((int)UserRole.HumanUser);
    }

    [Fact]
    public async Task PostUser_AsHumanUser_Returns403()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Human Forbidden Post", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);
        var payload = new { name = "Should Not Create", role = (int)UserRole.HumanUser };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/users", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostUser_AsAgentUser_Returns403()
    {
        // Arrange
        var agent = await TestAuthHelper.CreateUserAsync(_client, _factory, "Agent Forbidden Post", UserRole.AgentUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, agent.AuthKey);
        var payload = new { name = "Should Not Create", role = (int)UserRole.HumanUser };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/users", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_ReturnsUsersList()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<JsonElement[]>(_jsonOptions);
        users.ShouldNotBeNull();
        users.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GetUsers_AsHumanUser_Returns403()
    {
        // Arrange
        var human = await TestAuthHelper.CreateUserAsync(_client, _factory, "Human Forbidden Get", UserRole.HumanUser);
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, human.AuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Request_MissingApiKey_Returns401()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Remove("X-User-Key");
        _client.DefaultRequestHeaders.Add("X-User-Key", _factory.AdminAuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_WrongApiKey_Returns401()
    {
        // Arrange
        TestAuthHelper.SetAuth(_client, "wrong-api-key", _factory.AdminAuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_ValidApiKeyButInvalidUserKey_Returns401()
    {
        // Arrange
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, "nonexistent-user-key");

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

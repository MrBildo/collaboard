using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Models;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class UserEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly CollaboardApiFactory _factory;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public UserEndpointTests(CollaboardApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostUser_AsAdmin_Returns201WithUserDetails()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);
        var payload = new { name = "New Human", role = (int)UserRole.HumanUser };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/users", payload);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.True(json.TryGetProperty("id", out var idProp));
        Assert.NotEqual(Guid.Empty, idProp.GetGuid());
        Assert.True(json.TryGetProperty("authKey", out var authKeyProp));
        Assert.False(string.IsNullOrWhiteSpace(authKeyProp.GetString()));
        Assert.Equal("New Human", json.GetProperty("name").GetString());
        Assert.Equal((int)UserRole.HumanUser, json.GetProperty("role").GetInt32());
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
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_ReturnsUsersList()
    {
        // Arrange
        TestAuthHelper.SetAdminAuth(_client, _factory);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var users = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOptions);
        Assert.NotNull(users);
        Assert.NotEmpty(users);
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
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WrongApiKey_Returns401()
    {
        // Arrange
        TestAuthHelper.SetAuth(_client, "wrong-api-key", _factory.AdminAuthKey);

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_ValidApiKeyButInvalidUserKey_Returns401()
    {
        // Arrange
        TestAuthHelper.SetAuth(_client, CollaboardApiFactory.TestApiKey, "nonexistent-user-key");

        // Act
        var response = await _client.GetAsync("/api/v1/users");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

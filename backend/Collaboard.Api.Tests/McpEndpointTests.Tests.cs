using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Tests.Infrastructure;

namespace Collaboard.Api.Tests;

public class McpEndpointTests : IClassFixture<CollaboardApiFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public McpEndpointTests(CollaboardApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetMcp_Returns200WithExpectedManifestShape()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Remove("X-User-Key");

        // Act
        var response = await _client.GetAsync("/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("collaboard", json.GetProperty("name").GetString());
        Assert.Equal("modelcontextprotocol", json.GetProperty("protocol").GetString());
        Assert.True(json.TryGetProperty("description", out var descProp));
        Assert.False(string.IsNullOrWhiteSpace(descProp.GetString()));
        Assert.True(json.TryGetProperty("tools", out var toolsProp));
        Assert.Equal(JsonValueKind.Array, toolsProp.ValueKind);
        Assert.True(toolsProp.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetMcp_WorksWithoutAuthHeaders()
    {
        // Arrange
        var unauthenticatedClient = new HttpClient
        {
            BaseAddress = _client.BaseAddress,
        };
        unauthenticatedClient.DefaultRequestHeaders.Clear();

        // Act
        var response = await _client.GetAsync("/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("collaboard", json.GetProperty("name").GetString());
    }
}

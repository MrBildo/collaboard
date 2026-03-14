using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Collaboard.Api.Tests.Infrastructure;
using Shouldly;

namespace Collaboard.Api.Tests;

public class McpEndpointTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task GetMcp_Returns200WithExpectedManifestShape()
    {
        // Arrange
        _client.DefaultRequestHeaders.Remove("X-Api-Key");
        _client.DefaultRequestHeaders.Remove("X-User-Key");

        // Act
        var response = await _client.GetAsync("/mcp");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        json.GetProperty("name").GetString().ShouldBe("collaboard");
        json.GetProperty("protocol").GetString().ShouldBe("modelcontextprotocol");
        json.TryGetProperty("description", out var descProp).ShouldBeTrue();
        descProp.GetString().ShouldNotBeNullOrWhiteSpace();
        json.TryGetProperty("tools", out var toolsProp).ShouldBeTrue();
        toolsProp.ValueKind.ShouldBe(JsonValueKind.Array);
        toolsProp.GetArrayLength().ShouldBeGreaterThan(0);
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        json.GetProperty("name").GetString().ShouldBe("collaboard");
    }
}

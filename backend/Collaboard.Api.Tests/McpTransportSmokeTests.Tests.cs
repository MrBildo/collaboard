using System.Text.Json;
using Collaboard.Api.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace Collaboard.Api.Tests;

// The single sanctioned /mcp transport test (#251, per the #206 backend testing convention).
// Every other MCP test invokes the tool class directly and never touches the transport — so an
// SDK bump that silently drops the tool surface leaves them all green. This test drives a real
// MCP client over the /mcp HTTP transport to assert the wires are connected: the server boots and
// completes the initialize handshake, the full tool surface is exposed via tools/list, and one
// happy-path tools/call round-trips. It asserts transport PRESENCE, never per-tool behavior — the
// moment a second /mcp test checks a tool's logic through the transport, it rebuilds the duplicate
// matrix the convention rejects.
public class McpTransportSmokeTests(CollaboardApiFactory factory) : IClassFixture<CollaboardApiFactory>
{
    // The full tool surface (SystemTools + Board/Card/Archive/Comment/Attachment/Label/Lane/Size/
    // Prune/BulkCard/Search/Webhook). The exact count is the tripwire: an SDK bump that silently
    // drops tools trips it. Adding a tool is expected to bump this by hand — that one-line edit is
    // the intended signal. #269 added update_comment (CommentTools) and search_cards (SearchTools):
    // 35 -> 37.
    // #277 added reorder_lanes (LaneTools): 37 -> 38.
    // #308 added reorder_sizes (SizeTools): 38 -> 39.
    // #326 added WebhookTools (create/list/update/delete/test_webhook): 39 -> 44.
    // #357 added get_card_history (HistoryTools): 44 -> 45.
    private const int _expectedToolCount = 45;

    private readonly CollaboardApiFactory _factory = factory;

    [Fact]
    public async Task McpTransport_BootsListsToolsAndRoundTripsOneCall()
    {
        // Arrange — drive the real MCP client over the in-memory test server's HttpClient.
        // CreateAsync performs the initialize handshake; if the transport is broken it throws here.
        var httpClient = _factory.CreateClient();

        var transport = new HttpClientTransport
        (
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false
        );

        await using var client = await McpClient.CreateAsync(transport);

        // Act — list the tool surface and round-trip one happy-path call over the transport.
        var tools = await client.ListToolsAsync();

        var result = await client.CallToolAsync
        (
            "get_api_info",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["authKey"] = _factory.AdminAuthKey }
        );

        // Assert — the wires are connected: full surface present, one call round-trips.
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        tools.Count.ShouldBe(_expectedToolCount);
        toolNames.ShouldContain("get_api_info");
        toolNames.ShouldContain("get_boards");
        toolNames.ShouldContain("create_card");

        result.IsError.ShouldNotBe(true);

        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var payload = JsonSerializer.Deserialize<JsonElement>(text);
        payload.GetProperty("apiPrefix").GetString().ShouldBe("/api/v1");
    }
}

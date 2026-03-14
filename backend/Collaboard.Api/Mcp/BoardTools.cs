using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class BoardTools(BoardDbContext db, McpAuthService auth)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool(Name = "get_board", ReadOnly = true, Destructive = false)]
    [Description("Get the full kanban board including all lanes and their cards, ordered by position.")]
    public async Task<string> GetBoardAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lanes = await db.Lanes.OrderBy(l => l.Position).ToListAsync(ct);
        var cards = await db.Cards.OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync(ct);
        return JsonSerializer.Serialize(new { lanes, cards }, _jsonOptions);
    }

    [McpServerTool(Name = "get_api_info", ReadOnly = true, Destructive = false)]
    [Description("Get information about the REST API for operations not available via MCP tools (e.g., file uploads).")]
    public static string GetApiInfo() =>
        """
        Collaboard REST API (for operations not available via MCP):

        Base URL: Use the same host as the MCP server, under /api/v1/
        Auth: Include header "X-User-Key: <your-auth-key>" on all requests.

        File Attachments (not available via MCP — use REST):
          Upload:   POST /api/v1/cards/{cardId}/attachments  (multipart/form-data, field: "file")
          Download: GET  /api/v1/attachments/{id}
          List:     Available via MCP tool "get_attachments"
          Delete:   Available via MCP tool "delete_attachment"

        Other REST-only endpoints:
          PATCH /api/v1/cards/{id}         — partial update with JSON body
          GET   /api/v1/board              — full board state
          GET   /api/v1/users/directory    — list user names (all roles)

        All card/lane/label/comment operations are available via MCP tools.
        """;

    [McpServerTool(Name = "get_lanes", ReadOnly = true, Destructive = false)]
    [Description("Get all lanes (columns) on the board, ordered by position.")]
    public async Task<string> GetLanesAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lanes = await db.Lanes.OrderBy(l => l.Position).ToListAsync(ct);
        return JsonSerializer.Serialize(lanes, _jsonOptions);
    }
}

using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class BoardTools(BoardDbContext db, McpAuthService auth, IHttpContextAccessor httpContextAccessor)
{
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
        return JsonSerializer.Serialize(new { lanes, cards }, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_api_info", ReadOnly = true, Destructive = false)]
    [Description("Get the REST API base URL and documentation for operations not available via MCP (e.g., file downloads).")]
    public string GetApiInfo()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}/api/v1"
            : "http://localhost/api/v1";

        return "Collaboard REST API\n\n"
            + $"Base URL: {baseUrl}\n"
            + "Auth: Include header \"X-User-Key: <your-auth-key>\" on all requests.\n\n"
            + "File Attachments:\n"
            + "  Upload (small files, images): MCP tool \"upload_attachment\" (base64-encoded content)\n"
            + $"  Upload (large files, CSVs, PDFs): POST {baseUrl}/cards/<cardId>/attachments (multipart/form-data, field: \"file\")\n"
            + $"  Download: GET {baseUrl}/attachments/<id> (returns file with Content-Disposition)\n"
            + "  List: Available via MCP tool \"get_attachments\"\n"
            + "  Delete: Available via MCP tool \"delete_attachment\"\n\n"
            + "Other REST endpoints (also available via MCP tools):\n"
            + $"  GET  {baseUrl}/board — full board state\n"
            + $"  GET  {baseUrl}/users/directory — list user names\n"
            + $"  PATCH {baseUrl}/cards/<id> — partial card update\n\n"
            + "All card/lane/label/comment operations are available via MCP tools.";
    }

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
        return JsonSerializer.Serialize(lanes, JsonSerializerOptions.Web);
    }
}

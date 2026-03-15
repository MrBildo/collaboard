using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class BoardTools(BoardDbContext db, McpAuthService auth)
{
    [McpServerTool(Name = "get_boards", ReadOnly = true, Destructive = false)]
    [Description("List all boards. Use this to discover board IDs for scoping other tools.")]
    public async Task<string> GetBoardsAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var boards = await db.Boards.OrderBy(b => b.Name).ToListAsync(ct);
        return JsonSerializer.Serialize(boards, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_lanes", ReadOnly = true, Destructive = false)]
    [Description("Get all lanes (columns) ordered by position. Pass a boardId to scope to a single board, or omit to get all boards.")]
    public async Task<string> GetLanesAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        [Description("Optional board ID to scope results to a single board")] Guid? boardId = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var query = db.Lanes.AsQueryable();
        if (boardId.HasValue)
        {
            query = query.Where(l => l.BoardId == boardId.Value);
        }

        var lanes = await query.OrderBy(l => l.Position).ToListAsync(ct);
        return JsonSerializer.Serialize(lanes, JsonSerializerOptions.Web);
    }
}

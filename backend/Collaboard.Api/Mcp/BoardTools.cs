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
    [Description("Get all lanes (columns) for a board, ordered by position. Each lane includes a cardCount with the number of cards in that lane.")]
    public async Task<string> GetLanesAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        [Description("Board ID to scope results")] Guid boardId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lanes = await db.Lanes
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Position)
            .Select(l => new
            {
                l.Id,
                l.BoardId,
                l.Name,
                l.Position,
                CardCount = db.Cards.Count(c => c.LaneId == l.Id)
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(lanes, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_sizes", ReadOnly = true, Destructive = false)]
    [Description("Get all card sizes for a board, ordered by ordinal. Use this to discover valid size IDs/names when creating or updating cards.")]
    public async Task<string> GetSizesAsync(
        [Description("Your auth key (X-User-Key)")] string authKey,
        [Description("Board ID to scope results")] Guid boardId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var sizes = await db.CardSizes
            .Where(s => s.BoardId == boardId)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(sizes, JsonSerializerOptions.Web);
    }
}

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

        var cardCounts = await db.Cards
            .Where(c => c.BoardId == boardId && !c.IsTemp)
            .GroupBy(c => c.LaneId)
            .Select(g => new { LaneId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.LaneId, x => x.Count, ct);

        var lanes = await db.Lanes
            .Where(l => l.BoardId == boardId && !l.IsArchiveLane)
            .OrderBy(l => l.Position)
            .ToListAsync(ct);

        var result = lanes.Select(l => new
        {
            l.Id,
            l.BoardId,
            l.Name,
            l.Position,
            CardCount = cardCounts.GetValueOrDefault(l.Id, 0)
        }).ToList();

        return JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
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

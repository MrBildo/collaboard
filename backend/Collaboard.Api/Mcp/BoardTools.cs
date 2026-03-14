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
    public async Task<string> GetBoardAsync(CancellationToken ct)
    {
        var (user, error) = await auth.RequireUserAsync(ct);
        if (error is not null)
        {
            return error;
        }

        var lanes = await db.Lanes.OrderBy(l => l.Position).ToListAsync(ct);
        var cards = await db.Cards.OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync(ct);
        return JsonSerializer.Serialize(new { lanes, cards }, _jsonOptions);
    }

    [McpServerTool(Name = "get_lanes", ReadOnly = true, Destructive = false)]
    [Description("Get all lanes (columns) on the board, ordered by position.")]
    public async Task<string> GetLanesAsync(CancellationToken ct)
    {
        var (user, error) = await auth.RequireUserAsync(ct);
        if (error is not null)
        {
            return error;
        }

        var lanes = await db.Lanes.OrderBy(l => l.Position).ToListAsync(ct);
        return JsonSerializer.Serialize(lanes, _jsonOptions);
    }
}

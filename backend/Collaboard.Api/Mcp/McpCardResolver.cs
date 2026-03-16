using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Mcp;

internal static class McpCardResolver
{
    public static async Task<(Guid? CardId, string? Error)> ResolveCardIdAsync(
        BoardDbContext db, Guid? cardId, long? cardNumber,
        Guid? boardId = null, string? boardSlug = null,
        CancellationToken ct = default)
    {
        if (cardId.HasValue && cardNumber.HasValue)
        {
            return (null, "Error: Provide either cardId or cardNumber, not both.");
        }

        if (!cardId.HasValue && !cardNumber.HasValue)
        {
            return (null, "Error: Provide either cardId or cardNumber.");
        }

        if (cardId.HasValue)
        {
            return (cardId.Value, null);
        }

        // cardNumber requires board context
        var (resolvedBoardId, boardError) = await ResolveBoardIdAsync(db, boardId, boardSlug, ct);
        if (boardError is not null)
        {
            return (null, boardError);
        }

        var card = await db.Cards.FirstOrDefaultAsync(
            c => c.BoardId == resolvedBoardId && c.Number == cardNumber, ct);
        return card is not null
            ? (card.Id, null)
            : (null, $"Error: Card #{cardNumber} not found on this board.");
    }

    private static async Task<(Guid? BoardId, string? Error)> ResolveBoardIdAsync(
        BoardDbContext db, Guid? boardId, string? boardSlug, CancellationToken ct)
    {
        if (boardId.HasValue && !string.IsNullOrWhiteSpace(boardSlug))
        {
            return (null, "Error: Provide either boardId or boardSlug, not both.");
        }

        if (boardId.HasValue)
        {
            if (!await db.Boards.AnyAsync(b => b.Id == boardId.Value, ct))
            {
                return (null, "Error: Board not found.");
            }

            return (boardId.Value, null);
        }

        if (!string.IsNullOrWhiteSpace(boardSlug))
        {
            var board = await db.Boards.FirstOrDefaultAsync(b => b.Slug == boardSlug, ct);
            return board is not null
                ? (board.Id, null)
                : (null, $"Error: Board with slug '{boardSlug}' not found.");
        }

        return (null, "Error: boardId or boardSlug is required when using cardNumber.");
    }
}

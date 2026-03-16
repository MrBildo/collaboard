using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Events;

internal static class EventExtensions
{
    public static async Task PublishForCardAsync(this BoardDbContext db, Guid cardId, BoardEventBroadcaster broadcaster)
    {
        var boardId = await db.Cards
            .Where(c => c.Id == cardId)
            .Select(c => c.BoardId)
            .FirstOrDefaultAsync();

        if (boardId != Guid.Empty)
        {
            broadcaster.PublishBoardUpdated(boardId);
        }
    }
}

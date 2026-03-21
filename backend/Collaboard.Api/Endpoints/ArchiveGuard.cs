namespace Collaboard.Api.Endpoints;

internal static class ArchiveGuard
{
    public static async Task<bool> IsCardArchivedAsync(BoardDbContext db, Guid cardId)
    {
        var card = await db.Cards.FindAsync(cardId);
        if (card is null)
        {
            return false;
        }

        var lane = await db.Lanes.FindAsync(card.LaneId);
        return lane is not null && lane.IsArchiveLane;
    }
}

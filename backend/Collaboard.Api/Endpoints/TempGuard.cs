namespace Collaboard.Api.Endpoints;

internal static class TempGuard
{
    public static async Task<bool> IsCardTempAsync(BoardDbContext db, Guid cardId)
    {
        var card = await db.Cards.FindAsync(cardId);
        return card is not null && card.IsTemp;
    }
}

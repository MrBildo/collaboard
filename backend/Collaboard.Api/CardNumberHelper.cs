using Collaboard.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api;

internal static class CardNumberHelper
{
    public static async Task InsertCardWithAutoNumberAsync(
        BoardDbContext db,
        CardItem card,
        Guid boardId,
        CancellationToken ct = default)
    {
        const int maxRetries = 3;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            card.Number = (await db.Cards
                .Where(c => c.BoardId == boardId && c.Number > 0)
                .MaxAsync(c => (long?)c.Number, ct) ?? 0) + 1;

            db.Cards.Add(card);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex)
                when (attempt < maxRetries - 1
                      && ex.InnerException is SqliteException { SqliteErrorCode: 19 })
            {
                db.Entry(card).State = EntityState.Detached;
            }
        }

        throw new InvalidOperationException("Failed to allocate card number after retries.");
    }
}

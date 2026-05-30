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

    // Assigns a board-scoped card number to an existing temp card and clears the IsTemp
    // flag, retrying on unique-constraint collisions (SQLite error 19). The caller is
    // responsible for setting LastUpdatedAtUtc / LastUpdatedByUserId before calling.
    // Throws InvalidOperationException if all retries are exhausted.
    public static async Task FinalizeCardNumberAsync(
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
            card.IsTemp = false;
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex)
                when (attempt < maxRetries - 1
                      && ex.InnerException is SqliteException { SqliteErrorCode: 19 })
            {
                await db.Entry(card).ReloadAsync(ct);
            }
        }

        throw new InvalidOperationException("Failed to allocate card number after retries.");
    }
}

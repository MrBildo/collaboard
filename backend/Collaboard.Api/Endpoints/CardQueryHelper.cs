using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardQueryHelper
{
    public static IQueryable<CardItem> ApplySinceFilter(
        IQueryable<CardItem> query, BoardDbContext db, DateTimeOffset since)
    {
        // #234: "recent activity" = the card itself changed, OR it gained a
        // recent comment/attachment. The comment/attachment clauses are
        // correlated EXISTS sub-queries. These translate to SQL only because
        // the DateTimeOffset columns are stored as a sortable normalized-UTC
        // ISO-8601 string via a value converter (see
        // BoardDbContext.OnModelCreating) — SQLite cannot translate a
        // DateTimeOffset comparison in a nested query position without it. The
        // whole filter runs server-side; nothing is materialized client-side.
        return query.Where(x =>
            x.CreatedAtUtc >= since
            || x.LastUpdatedAtUtc >= since
            || db.Comments.Any(c => c.CardId == x.Id && c.LastUpdatedAtUtc >= since)
            || db.Attachments.Any(a => a.CardId == x.Id && a.AddedAtUtc >= since));
    }
}

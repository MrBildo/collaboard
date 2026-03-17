using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardQueryHelper
{
    public static IQueryable<CardItem> ApplySinceFilter(
        IQueryable<CardItem> query, BoardDbContext db, DateTimeOffset since)
    {
        var cardIdsWithRecentComments = db.Comments
            .Where(c => c.LastUpdatedAtUtc >= since)
            .Select(c => c.CardId);
        var cardIdsWithRecentAttachments = db.Attachments
            .Where(a => a.AddedAtUtc >= since)
            .Select(a => a.CardId);

        return query.Where(x =>
            x.CreatedAtUtc >= since
            || x.LastUpdatedAtUtc >= since
            || cardIdsWithRecentComments.Contains(x.Id)
            || cardIdsWithRecentAttachments.Contains(x.Id));
    }
}

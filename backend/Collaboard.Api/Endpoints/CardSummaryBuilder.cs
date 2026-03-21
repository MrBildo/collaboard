using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardSummaryBuilder
{
    public static async Task<List<CardSummary>> BuildAsync(BoardDbContext db, List<CardItem> cards, CancellationToken ct = default)
    {
        if (cards.Count == 0)
        {
            return [];
        }

        var cardIds = cards.Select(c => c.Id).ToList();

        // Batch load sizes
        var sizeIds = cards.Select(c => c.SizeId).Distinct().ToList();
        var sizeNames = await db.CardSizes
            .Where(s => sizeIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // Batch load labels
        var cardLabels = await db.CardLabels
            .Where(cl => cardIds.Contains(cl.CardId))
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => new { cl.CardId, Label = new CardLabelSummary(l.Id, l.Name, l.Color) })
            .ToListAsync(ct);
        var labelsByCard = cardLabels.GroupBy(x => x.CardId).ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());

        // Batch load counts
        var commentCounts = await db.Comments
            .Where(cm => cardIds.Contains(cm.CardId))
            .GroupBy(cm => cm.CardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, ct);

        var attachmentCounts = await db.Attachments
            .Where(a => cardIds.Contains(a.CardId))
            .GroupBy(a => a.CardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, ct);

        // Batch load archive lane IDs for the boards in the result set
        var boardIds = cards.Select(c => c.BoardId).Distinct().ToList();
        var archiveLaneIds = await db.Lanes
            .Where(l => boardIds.Contains(l.BoardId) && l.IsArchiveLane)
            .Select(l => l.Id)
            .ToHashSetAsync(ct);

        // Project
        return [.. cards.Select(c => new CardSummary(
            c.Id, c.Number, c.Name, c.DescriptionMarkdown,
            c.SizeId, sizeNames.GetValueOrDefault(c.SizeId, "?"),
            c.LaneId, c.Position,
            c.CreatedByUserId, c.CreatedAtUtc,
            c.LastUpdatedByUserId, c.LastUpdatedAtUtc,
            labelsByCard.GetValueOrDefault(c.Id, []),
            commentCounts.GetValueOrDefault(c.Id, 0),
            attachmentCounts.GetValueOrDefault(c.Id, 0),
            archiveLaneIds.Contains(c.LaneId)
        ))];
    }
}

using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search/cards", async (BoardDbContext db, string? q, int? limit) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<SearchResult>());
            }

            var effectiveLimit = Math.Clamp(limit ?? 20, 1, 50);

            var query = db.Cards.AsQueryable();
            query = SearchHelper.ApplySearchFilter(query, q);

            var cards = await query
                .OrderBy(c => c.BoardId)
                .ThenByDescending(c => c.Number)
                .Take(effectiveLimit)
                .ToListAsync();

            if (cards.Count == 0)
            {
                return Results.Ok(Array.Empty<SearchResult>());
            }

            var cardIds = cards.Select(c => c.Id).ToList();
            var boardIds = cards.Select(c => c.BoardId).Distinct().ToList();

            // Batch load boards
            var boards = await db.Boards
                .Where(b => boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b);

            // Batch load sizes
            var sizeIds = cards.Select(c => c.SizeId).Distinct().ToList();
            var sizeNames = await db.CardSizes
                .Where(s => sizeIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            // Batch load labels
            var cardLabels = await db.CardLabels
                .Where(cl => cardIds.Contains(cl.CardId))
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => new { cl.CardId, Label = new CardLabelSummary(l.Id, l.Name, l.Color) })
                .ToListAsync();
            var labelsByCard = cardLabels.GroupBy(x => x.CardId).ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());

            // Batch load counts
            var commentCounts = await db.Comments
                .Where(cm => cardIds.Contains(cm.CardId))
                .GroupBy(cm => cm.CardId)
                .Select(g => new { CardId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CardId, x => x.Count);

            var attachmentCounts = await db.Attachments
                .Where(a => cardIds.Contains(a.CardId))
                .GroupBy(a => a.CardId)
                .Select(g => new { CardId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CardId, x => x.Count);

            // Build a cardId -> boardId lookup
            var cardBoardMap = cards.ToDictionary(c => c.Id, c => c.BoardId);

            // Project to summaries
            var summaries = cards.Select(c => new CardSummary(
                c.Id, c.Number, c.Name, c.DescriptionMarkdown,
                c.SizeId, sizeNames.GetValueOrDefault(c.SizeId, "?"),
                c.LaneId, c.Position,
                c.CreatedByUserId, c.CreatedAtUtc,
                c.LastUpdatedByUserId, c.LastUpdatedAtUtc,
                labelsByCard.GetValueOrDefault(c.Id, []),
                commentCounts.GetValueOrDefault(c.Id, 0),
                attachmentCounts.GetValueOrDefault(c.Id, 0)
            )).ToList();

            // Group by board
            var results = summaries
                .GroupBy(s => cardBoardMap[s.Id])
                .Where(g => boards.ContainsKey(g.Key))
                .Select(g =>
                {
                    var board = boards[g.Key];
                    return new SearchResult(board.Id, board.Name, board.Slug, [.. g]);
                })
                .ToList();

            return Results.Ok(results);
        }).RequireAuth();

        return group;
    }
}

using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/search/cards", async (BoardDbContext db, string? q, int? limit, Guid? archiveBoardId, Guid? boardId) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<SearchResult>());
            }

            var effectiveLimit = Math.Clamp(limit ?? 20, 1, 50);

            // Load all archive lane IDs upfront (spans all boards)
            var allArchiveLanes = await db.Lanes
                .Where(l => l.IsArchiveLane)
                    .Select(l => new { l.Id, l.BoardId })
                        .ToListAsync();

            // Separate: exclude archive lanes from all boards except the archiveBoardId
            var excludeArchiveLaneIds = allArchiveLanes
                .Where(l => l.BoardId != archiveBoardId)
                    .Select(l => l.Id)
                        .ToList();

            var query = db.Cards.Where(c => !c.IsTemp);
            query = SearchHelper.ApplySearchFilter(query, q);

            // Exclude archived cards (except those from archiveBoardId)
            if (excludeArchiveLaneIds.Count > 0)
            {
                query = query.Where(c => !excludeArchiveLaneIds.Contains(c.LaneId));
            }

            var cards = await query
                .OrderBy(c => c.BoardId)
                .ThenByDescending(c => c.Number)
                .Take(effectiveLimit)
                    .ToListAsync();

            if (cards.Count == 0)
            {
                return Results.Ok(Array.Empty<SearchResult>());
            }

            var boardIds = cards.Select(c => c.BoardId).Distinct().ToList();

            // Batch load boards
            var boards = await db.Boards
                .Where(b => boardIds.Contains(b.Id))
                    .ToDictionaryAsync(b => b.Id, b => b);

            // Build a cardId -> boardId lookup (needed for the board grouping below).
            var cardBoardMap = cards.ToDictionary(c => c.Id, c => c.BoardId);

            // One shared projection across surfaces — the builder owns sizes, labels,
            // counts, archive flag, and the latest-comment enrichment (#274). Keeping
            // search on the builder avoids a second CardSummary projection drifting.
            var summaries = await CardSummaryBuilder.BuildAsync(db, cards);

            // Group by board; current board (if specified) ranks first
            var results = summaries
                .GroupBy(s => cardBoardMap[s.Id])
                .Where(g => boards.ContainsKey(g.Key))
                    .Select(g =>
                    {
                        var board = boards[g.Key];
                        return new SearchResult(board.Id, board.Name, board.Slug, [.. g]);
                    })
                    .OrderBy(r => r.BoardId == boardId ? 0 : 1)
                        .ToList();

            return Results.Ok(results);
        }).RequireAuth();

        return group;
    }
}

using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class PruneEndpoints
{
    public static RouteGroupBuilder MapPruneEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/boards/{boardId:guid}/prune/preview", async (BoardDbContext db, Guid boardId, PruneRequest request) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (!ValidateFilters(request, out var error))
            {
                return Results.BadRequest(error);
            }

            var query = BuildFilteredQuery(db, boardId, request);
            var cards = await query.ToListAsync();

            // Batch load lane names
            var laneIdSet = cards.Select(c => c.LaneId).Distinct().ToList();
            var laneNames = await db.Lanes
                .Where(l => laneIdSet.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name);

            var cardSummaries = cards.Select(c => new
            {
                c.Id,
                c.Number,
                c.Name,
                laneName = laneNames.GetValueOrDefault(c.LaneId, "?"),
                c.LastUpdatedAtUtc,
            }).ToList();

            return Results.Ok(new { matchCount = cards.Count, cards = cardSummaries });
        }).RequireAdmin();

        group.MapPost("/boards/{boardId:guid}/prune", async (BoardDbContext db, Guid boardId, PruneRequest request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (!ValidateFilters(request, out var error))
            {
                return Results.BadRequest(error);
            }

            var query = BuildFilteredQuery(db, boardId, request);
            var cards = await query.ToListAsync();

            db.Cards.RemoveRange(cards);
            await db.SaveChangesAsync();

            broadcaster.PublishBoardUpdated(boardId);

            return Results.Ok(new { deletedCount = cards.Count });
        }).RequireAdmin();

        return group;
    }

    private static bool ValidateFilters(PruneRequest request, out string? error)
    {
        error = null;

        var hasAnyFilter = request.OlderThan is not null
            || (request.LaneIds is not null && request.LaneIds.Length > 0)
            || (request.LabelIds is not null && request.LabelIds.Length > 0);

        if (!hasAnyFilter)
        {
            error = "At least one filter is required (olderThan, laneIds, or labelIds).";
            return false;
        }

        return true;
    }

    private static IQueryable<CardItem> BuildFilteredQuery(
        BoardDbContext db,
        Guid boardId,
        PruneRequest request)
    {
        var query = db.Cards.Where(c => c.BoardId == boardId);

        if (request.OlderThan.HasValue)
        {
            var cutoffIds = db.Cards
                .FromSqlInterpolated($"SELECT * FROM Cards WHERE LastUpdatedAtUtc < {request.OlderThan.Value.ToString("O")}")
                .Select(c => c.Id);
            query = query.Where(c => cutoffIds.Contains(c.Id));
        }

        if (request.LaneIds is not null && request.LaneIds.Length > 0)
        {
            var laneIds = request.LaneIds.ToList();
            query = query.Where(c => laneIds.Contains(c.LaneId));
        }

        if (request.LabelIds is not null && request.LabelIds.Length > 0)
        {
            var labelIds = request.LabelIds.ToList();
            var cardIdsWithLabels = db.CardLabels
                .Where(cl => labelIds.Contains(cl.LabelId))
                .Select(cl => cl.CardId);

            query = query.Where(c => cardIdsWithLabels.Contains(c.Id));
        }

        return query;
    }
}

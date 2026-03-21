using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class PruneEndpoints
{
    private static readonly string[] _validActions = ["archive", "delete"];

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

            var query = await BuildFilteredQueryAsync(db, boardId, request);
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

            if (!ValidateAction(request.Action, out var actionError))
            {
                return Results.BadRequest(actionError);
            }

            var action = string.IsNullOrEmpty(request.Action) ? "archive" : request.Action;

            var query = await BuildFilteredQueryAsync(db, boardId, request);
            var cards = await query.ToListAsync();

            if (action == "archive")
            {
                var archiveLane = await db.Lanes.FirstOrDefaultAsync(l => l.BoardId == boardId && l.IsArchiveLane);
                if (archiveLane is null)
                {
                    return Results.BadRequest("Board has no archive lane.");
                }

                foreach (var card in cards)
                {
                    await CardReorderHelper.MoveCardToLaneAsync(db, card, archiveLane.Id, 0);
                }

                await db.SaveChangesAsync();
                broadcaster.PublishBoardUpdated(boardId);

                return Results.Ok(new { archivedCount = cards.Count });
            }
            else
            {
                db.Cards.RemoveRange(cards);
                await db.SaveChangesAsync();

                broadcaster.PublishBoardUpdated(boardId);

                return Results.Ok(new { deletedCount = cards.Count });
            }
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

    private static bool ValidateAction(string? action, out string? error)
    {
        error = null;

        if (string.IsNullOrEmpty(action))
        {
            return true;
        }

        if (!_validActions.Contains(action))
        {
            error = $"Invalid action '{action}'. Valid actions are: archive, delete.";
            return false;
        }

        return true;
    }

    private static async Task<IQueryable<CardItem>> BuildFilteredQueryAsync(
        BoardDbContext db,
        Guid boardId,
        PruneRequest request)
    {
        var query = db.Cards.Where(c => c.BoardId == boardId);

        // Exclude archived cards by default
        if (request.IncludeArchived is not true)
        {
            var archiveLaneIds = await db.Lanes
                .Where(l => l.BoardId == boardId && l.IsArchiveLane)
                .Select(l => l.Id)
                .ToListAsync();

            if (archiveLaneIds.Count > 0)
            {
                query = query.Where(c => !archiveLaneIds.Contains(c.LaneId));
            }
        }

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

using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class PruneEndpoints
{
    public static RouteGroupBuilder MapPruneEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/boards/{boardId:guid}/prune/preview", async (BoardDbContext db, Guid boardId, JsonElement body) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (!TryParseFilters(body, out var olderThan, out var laneIds, out var labelIds, out var error))
            {
                return Results.BadRequest(error);
            }

            var query = BuildFilteredQuery(db, boardId, olderThan, laneIds, labelIds);
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

        group.MapPost("/boards/{boardId:guid}/prune", async (BoardDbContext db, Guid boardId, JsonElement body, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (!TryParseFilters(body, out var olderThan, out var laneIds, out var labelIds, out var error))
            {
                return Results.BadRequest(error);
            }

            var query = BuildFilteredQuery(db, boardId, olderThan, laneIds, labelIds);
            var cards = await query.ToListAsync();

            db.Cards.RemoveRange(cards);
            await db.SaveChangesAsync();

            broadcaster.PublishBoardUpdated(boardId);

            return Results.Ok(new { deletedCount = cards.Count });
        }).RequireAdmin();

        return group;
    }

    private static bool TryParseFilters(
        JsonElement body,
        out DateTimeOffset? olderThan,
        out List<Guid>? laneIds,
        out List<Guid>? labelIds,
        out string? error)
    {
        olderThan = null;
        laneIds = null;
        labelIds = null;
        error = null;

        var hasAnyFilter = false;

        if (body.TryGetProperty("olderThan", out var olderThanProp) && olderThanProp.ValueKind != JsonValueKind.Null)
        {
            if (!DateTimeOffset.TryParse(olderThanProp.GetString(), out var parsed))
            {
                error = "olderThan must be a valid ISO 8601 date.";
                return false;
            }

            olderThan = parsed;
            hasAnyFilter = true;
        }

        if (body.TryGetProperty("laneIds", out var laneIdsProp) && laneIdsProp.ValueKind == JsonValueKind.Array)
        {
            laneIds = [.. laneIdsProp.EnumerateArray().Select(e => e.GetGuid())];
            if (laneIds.Count > 0)
            {
                hasAnyFilter = true;
            }
        }

        if (body.TryGetProperty("labelIds", out var labelIdsProp) && labelIdsProp.ValueKind == JsonValueKind.Array)
        {
            labelIds = [.. labelIdsProp.EnumerateArray().Select(e => e.GetGuid())];
            if (labelIds.Count > 0)
            {
                hasAnyFilter = true;
            }
        }

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
        DateTimeOffset? olderThan,
        List<Guid>? laneIds,
        List<Guid>? labelIds)
    {
        var query = db.Cards.Where(c => c.BoardId == boardId);

        if (olderThan.HasValue)
        {
            var cutoffIds = db.Cards
                .FromSqlInterpolated($"SELECT * FROM Cards WHERE LastUpdatedAtUtc < {olderThan.Value.ToString("O")}")
                .Select(c => c.Id);
            query = query.Where(c => cutoffIds.Contains(c.Id));
        }

        if (laneIds is not null && laneIds.Count > 0)
        {
            query = query.Where(c => laneIds.Contains(c.LaneId));
        }

        if (labelIds is not null && labelIds.Count > 0)
        {
            var cardIdsWithLabels = db.CardLabels
                .Where(cl => labelIds.Contains(cl.LabelId))
                .Select(cl => cl.CardId);

            query = query.Where(c => cardIdsWithLabels.Contains(c.Id));
        }

        return query;
    }
}

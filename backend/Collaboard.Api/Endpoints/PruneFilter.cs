using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

// Shared prune-filter logic for the REST PruneEndpoints and the MCP PruneTools.
// Card #243 Phase 4 extracted this out of PruneEndpoints so both surfaces build
// the filtered card query from a single source — REST/MCP drift is the top bug
// class on this codebase, and prune's match semantics (archive exclusion, the
// olderThan raw-SQL TEXT comparison, lane/label filters) must stay identical
// across both entry points.
internal static class PruneFilter
{
    public const string NoFilterError = "At least one filter is required (olderThan, laneIds, or labelIds).";

    public static bool ValidateFilters(PruneRequest request, out string? error)
    {
        error = null;

        var hasAnyFilter = request.OlderThan is not null
            || (request.LaneIds is not null && request.LaneIds.Length > 0)
            || (request.LabelIds is not null && request.LabelIds.Length > 0);

        if (!hasAnyFilter)
        {
            error = NoFilterError;
            return false;
        }

        return true;
    }

    public static async Task<IQueryable<CardItem>> BuildFilteredQueryAsync(
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

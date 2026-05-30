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

            if (!PruneFilter.ValidateFilters(request, out var error))
            {
                return Results.BadRequest(error);
            }

            var query = PruneFilter.BuildFilteredQuery(db, boardId, request);
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
        }).RequireAdminOrAgentAdmin();

        group.MapPost("/boards/{boardId:guid}/prune", async (HttpContext http, BoardDbContext db, Guid boardId, PruneRequest request, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId, ct))
            {
                return Results.NotFound();
            }

            if (!PruneFilter.ValidateFilters(request, out var error))
            {
                return Results.BadRequest(error);
            }

            if (!ValidateAction(request.Action, out var actionError))
            {
                return Results.BadRequest(actionError);
            }

            var action = string.IsNullOrEmpty(request.Action) ? "archive" : request.Action;

            // AgentAdministrator is blocked from the destructive delete action.
            // Bulk delete is named in card #243's exclusion list; only Administrator may invoke it.
            if (action == "delete" && http.CurrentUser().Role == UserRole.AgentAdministrator)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var query = PruneFilter.BuildFilteredQuery(db, boardId, request);
            var cards = await query.ToListAsync(ct);

            if (action == "archive")
            {
                var archiveLane = await db.Lanes.FirstOrDefaultAsync(l => l.BoardId == boardId && l.IsArchiveLane, ct);
                if (archiveLane is null)
                {
                    return Results.BadRequest("Board has no archive lane.");
                }

                foreach (var card in cards)
                {
                    await CardReorderHelper.MoveCardToLaneAsync(db, card, archiveLane.Id, 0, ct);
                }

                await db.SaveChangesAsync(ct);
                broadcaster.PublishBoardUpdated(boardId);

                return Results.Ok(new { archivedCount = cards.Count });
            }
            else
            {
                db.Cards.RemoveRange(cards);
                await db.SaveChangesAsync(ct);

                broadcaster.PublishBoardUpdated(boardId);

                return Results.Ok(new { deletedCount = cards.Count });
            }
        }).RequireAdminOrAgentAdmin();

        return group;
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
}

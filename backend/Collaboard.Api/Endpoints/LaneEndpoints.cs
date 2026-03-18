using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class LaneEndpoints
{
    public static RouteGroupBuilder MapLaneEndpoints(this RouteGroupBuilder group)
    {
        // Board-scoped listing and creation
        group.MapGet("/boards/{boardId:guid}/lanes", async (BoardDbContext db, Guid boardId) =>
            !await db.Boards.AnyAsync(x => x.Id == boardId)
                ? Results.NotFound()
                : Results.Ok(await db.Lanes.Where(x => x.BoardId == boardId).OrderBy(x => x.Position).ToListAsync()))
            .RequireAuth();

        group.MapPost("/boards/{boardId:guid}/lanes", async (BoardDbContext db, Guid boardId, CreateLaneRequest request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var lane = new Lane { Id = Guid.NewGuid(), BoardId = boardId, Name = request.Name, Position = request.Position };
            db.Lanes.Add(lane);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(boardId);
            return Results.Created($"/api/v1/lanes/{lane.Id}", lane);
        }).RequireAdmin();

        // By-ID operations (flat)
        group.MapGet("/lanes/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var lane = await db.Lanes.FindAsync(id);
            return lane is null ? Results.NotFound() : Results.Ok(lane);
        }).RequireAuth();

        group.MapDelete("/lanes/{id:guid}", async (BoardDbContext db, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var lane = await db.Lanes.FindAsync(id);
            if (lane is null)
            {
                return Results.NotFound();
            }

            if (await db.Cards.AnyAsync(x => x.LaneId == id))
            {
                return Results.Conflict("Lane must be empty.");
            }

            db.Lanes.Remove(lane);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(lane.BoardId);
            return Results.NoContent();
        }).RequireAdmin();

        group.MapPatch("/lanes/{id:guid}", async (BoardDbContext db, Guid id, UpdateLaneRequest request, BoardEventBroadcaster broadcaster) =>
        {
            var lane = await db.Lanes.FindAsync(id);
            if (lane is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                lane.Name = request.Name;
            }

            if (request.Position is not null)
            {
                var newPos = request.Position.Value;
                if (await db.Lanes.AnyAsync(x => x.BoardId == lane.BoardId && x.Position == newPos && x.Id != id))
                {
                    return Results.Conflict("Position already taken by another lane.");
                }

                lane.Position = newPos;
            }

            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(lane.BoardId);
            return Results.Ok(lane);
        }).RequireAdmin();

        return group;
    }
}

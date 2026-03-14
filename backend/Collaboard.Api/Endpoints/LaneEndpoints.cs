using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class LaneEndpoints
{
    public static RouteGroupBuilder MapLaneEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/lanes", async (BoardDbContext db, HttpContext http, Lane request) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var lane = new Lane { Id = Guid.NewGuid(), Name = request.Name, Position = request.Position };
            db.Lanes.Add(lane);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/lanes/{lane.Id}", lane);
        });

        group.MapDelete("/lanes/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

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
            return Results.NoContent();
        });

        return group;
    }
}

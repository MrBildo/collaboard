using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class LaneEndpoints
{
    public static RouteGroupBuilder MapLaneEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/lanes", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            return forbidden is not null ? forbidden : Results.Ok(await db.Lanes.OrderBy(x => x.Position).ToListAsync());
        });

        group.MapGet("/lanes/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var lane = await db.Lanes.FindAsync(id);
            return lane is null ? Results.NotFound() : Results.Ok(lane);
        });

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

        group.MapPatch("/lanes/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch) =>
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

            if (patch.TryGetProperty("name", out var name))
            {
                lane.Name = name.GetString()!;
            }

            if (patch.TryGetProperty("position", out var pos))
            {
                var newPos = pos.GetInt32();
                if (await db.Lanes.AnyAsync(x => x.Position == newPos && x.Id != id))
                {
                    return Results.Conflict("Position already taken by another lane.");
                }

                lane.Position = newPos;
            }

            await db.SaveChangesAsync();
            return Results.Ok(lane);
        });

        return group;
    }
}

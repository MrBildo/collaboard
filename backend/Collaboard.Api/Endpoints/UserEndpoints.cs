using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/users", async (BoardDbContext db, HttpContext http, BoardUser request) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var user = new BoardUser
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Role = request.Role,
                AuthKey = Ulid.NewUlid().ToString(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/users/{user.Id}", user);
        });

        group.MapGet("/users", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            return forbidden is not null ? forbidden : Results.Ok(await db.Users.OrderBy(x => x.Name).ToListAsync());
        });

        group.MapPatch("/users/{id:guid}/deactivate", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var user = await db.Users.FindAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            user.IsActive = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return group;
    }
}

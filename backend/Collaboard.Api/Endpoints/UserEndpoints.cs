using System.Text.Json;
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

        group.MapGet("/users/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var user = await db.Users.FindAsync(id);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        group.MapGet("/users", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            return forbidden is not null ? forbidden : Results.Ok(await db.Users.OrderBy(x => x.Name).ToListAsync());
        });

        group.MapGet("/users/directory", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var users = await db.Users
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync();
            return Results.Ok(users);
        });

        group.MapPatch("/users/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch) =>
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

            if (patch.TryGetProperty("name", out var name))
            {
                user.Name = name.GetString()!;
            }

            if (patch.TryGetProperty("role", out var role))
            {
                user.Role = (UserRole)role.GetInt32();
            }

            await db.SaveChangesAsync();
            return Results.Ok(user);
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

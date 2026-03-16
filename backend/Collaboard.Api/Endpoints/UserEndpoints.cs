using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/users", async (BoardDbContext db, BoardUser request, BoardEventBroadcaster broadcaster) =>
        {
            var user = new BoardUser
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Role = request.Role,
                AuthKey = Ulid.NewUlid().ToString(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            broadcaster.PublishGlobal("board-updated");
            return Results.Created($"/api/v1/users/{user.Id}", user);
        }).RequireAdmin();

        group.MapGet("/users/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var user = await db.Users.FindAsync(id);
            return user is null ? Results.NotFound() : Results.Ok(user);
        }).RequireAdmin();

        group.MapGet("/users", async (BoardDbContext db) =>
            Results.Ok(await db.Users.OrderBy(x => x.Name).ToListAsync()))
            .RequireAdmin();

        group.MapGet("/users/directory", async (BoardDbContext db) =>
        {
            var users = await db.Users
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync();
            return Results.Ok(users);
        }).RequireAuth();

        group.MapPatch("/users/{id:guid}", async (BoardDbContext db, Guid id, JsonElement patch, BoardEventBroadcaster broadcaster) =>
        {
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
            broadcaster.PublishGlobal("board-updated");
            return Results.Ok(user);
        }).RequireAdmin();

        group.MapPatch("/users/{id:guid}/deactivate", async (BoardDbContext db, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            user.IsActive = false;
            await db.SaveChangesAsync();
            broadcaster.PublishGlobal("board-updated");
            return Results.NoContent();
        }).RequireAdmin();

        group.MapGet("/auth/me", (HttpContext http) =>
        {
            var user = http.CurrentUser();
            return Results.Ok(new { user.Id, user.Name, user.Role });
        }).RequireAuth();

        return group;
    }
}

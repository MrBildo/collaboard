using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class BoardEndpoints
{
    public static RouteGroupBuilder MapBoardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/boards", async (BoardDbContext db) =>
            Results.Ok(await db.Boards.OrderBy(x => x.Name).ToListAsync()))
            .RequireAuth();

        group.MapGet("/boards/{idOrSlug}", async (BoardDbContext db, string idOrSlug) =>
        {
            var board = Guid.TryParse(idOrSlug, out var id)
                ? await db.Boards.FindAsync(id)
                : await db.Boards.FirstOrDefaultAsync(x => x.Slug == idOrSlug);

            return board is null ? Results.NotFound() : Results.Ok(board);
        }).RequireAuth();

        group.MapPost("/boards", async (BoardDbContext db, JsonElement body) =>
        {
            if (!body.TryGetProperty("name", out var nameProp) || string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                return Results.BadRequest("Name is required.");
            }

            var name = nameProp.GetString()!;
            var slug = Board.GenerateSlug(name);

            if (await db.Boards.AnyAsync(x => x.Slug == slug))
            {
                return Results.Conflict("A board with that slug already exists.");
            }

            var board = new Board
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Boards.Add(board);

            db.CardSizes.AddRange(
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "S", Ordinal = 0 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "M", Ordinal = 1 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "L", Ordinal = 2 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "XL", Ordinal = 3 }
            );

            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/boards/{board.Id}", board);
        }).RequireAdmin();

        group.MapPatch("/boards/{id:guid}", async (BoardDbContext db, Guid id, JsonElement patch) =>
        {
            var board = await db.Boards.FindAsync(id);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (patch.TryGetProperty("name", out var name))
            {
                var nameStr = name.ValueKind == JsonValueKind.Null ? null : name.GetString();
                if (string.IsNullOrWhiteSpace(nameStr))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                board.Name = nameStr;
            }

            await db.SaveChangesAsync();
            return Results.Ok(board);
        }).RequireAdmin();

        group.MapDelete("/boards/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var board = await db.Boards.FindAsync(id);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (await db.Lanes.AnyAsync(x => x.BoardId == id))
            {
                return Results.BadRequest("Board must have no lanes before it can be deleted.");
            }

            db.Boards.Remove(board);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();

        // Composite board view — lanes + cards for a specific board
        group.MapGet("/boards/{boardId:guid}/board", async (BoardDbContext db, Guid boardId) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            var lanes = await db.Lanes.Where(x => x.BoardId == boardId).OrderBy(x => x.Position).ToListAsync();
            var cards = await db.Cards.Where(x => x.BoardId == boardId).OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();
            var sizes = await db.CardSizes.Where(x => x.BoardId == boardId).OrderBy(x => x.Ordinal).ToListAsync();
            return Results.Ok(new { lanes, cards, sizes });
        }).RequireAuth();

        return group;
    }
}

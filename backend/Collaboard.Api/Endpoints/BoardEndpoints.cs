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
                : await db.Boards.SingleOrDefaultAsync(x => x.Slug == idOrSlug);

            return board is null ? Results.NotFound() : Results.Ok(board);
        }).RequireAuth();

        group.MapPost("/boards", async (BoardDbContext db, CreateBoardRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var name = request.Name;
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

            db.Lanes.Add(new Lane
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Name = "Archive",
                Position = int.MaxValue,
                IsArchiveLane = true,
            });

            db.CardSizes.AddRange(
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "S", Ordinal = 0 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "M", Ordinal = 1 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "L", Ordinal = 2 },
                new CardSize { Id = Guid.NewGuid(), BoardId = board.Id, Name = "XL", Ordinal = 3 }
            );

            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/boards/{board.Id}", board);
        }).RequireAdmin();

        group.MapPatch("/boards/{id:guid}", async (BoardDbContext db, Guid id, UpdateBoardRequest request) =>
        {
            var board = await db.Boards.FindAsync(id);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                board.Name = request.Name;
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

            if (await db.Lanes.AnyAsync(x => x.BoardId == id && !x.IsArchiveLane))
            {
                return Results.BadRequest("Board must have no lanes before it can be deleted.");
            }

            var archivedCardsDeleted = await db.Cards.CountAsync(x => x.BoardId == id);

            db.Boards.Remove(board);
            await db.SaveChangesAsync();

            return archivedCardsDeleted > 0 ? Results.Ok(new { deleted = true, archivedCardsDeleted }) : Results.NoContent();
        }).RequireAdmin();

        // Composite board view — lanes + cards for a specific board
        group.MapGet("/boards/{boardId:guid}/board", async (BoardDbContext db, Guid boardId) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            var archiveLaneIds = await db.Lanes
                .Where(x => x.BoardId == boardId && x.IsArchiveLane)
                .Select(x => x.Id)
                .ToListAsync();

            var lanes = await db.Lanes.Where(x => x.BoardId == boardId && !x.IsArchiveLane).OrderBy(x => x.Position).ToListAsync();
            var rawCards = await db.Cards.Where(x => x.BoardId == boardId && !archiveLaneIds.Contains(x.LaneId)).OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();
            var cards = await CardSummaryBuilder.BuildAsync(db, rawCards);
            var sizes = await db.CardSizes.Where(x => x.BoardId == boardId).OrderBy(x => x.Ordinal).ToListAsync();
            return Results.Ok(new { lanes, cards, sizes });
        }).RequireAuth();

        return group;
    }
}

using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class SizeEndpoints
{
    public static RouteGroupBuilder MapSizeEndpoints(this RouteGroupBuilder group)
    {
        // Board-scoped listing and creation
        group.MapGet("/boards/{boardId:guid}/sizes", async (BoardDbContext db, Guid boardId) =>
            !await db.Boards.AnyAsync(x => x.Id == boardId)
                ? Results.NotFound()
                : Results.Ok(await db.CardSizes.Where(x => x.BoardId == boardId).OrderBy(x => x.Ordinal).ToListAsync()))
            .RequireAuth();

        group.MapPost("/boards/{boardId:guid}/sizes", async (BoardDbContext db, Guid boardId, CreateSizeRequest request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var ordinal = request.Ordinal ?? 0;
            if (!request.Ordinal.HasValue && await db.CardSizes.AnyAsync(x => x.BoardId == boardId))
            {
                ordinal = await db.CardSizes.Where(x => x.BoardId == boardId).MaxAsync(x => x.Ordinal) + 1;
            }

            var size = new CardSize { Id = Guid.NewGuid(), BoardId = boardId, Name = request.Name, Ordinal = ordinal };
            db.CardSizes.Add(size);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(boardId);
            return Results.Created($"/api/v1/sizes/{size.Id}", size);
        }).RequireAdmin();

        // By-ID operations (flat)
        group.MapGet("/sizes/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var size = await db.CardSizes.FindAsync(id);
            return size is null ? Results.NotFound() : Results.Ok(size);
        }).RequireAuth();

        group.MapDelete("/sizes/{id:guid}", async (BoardDbContext db, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var size = await db.CardSizes.FindAsync(id);
            if (size is null)
            {
                return Results.NotFound();
            }

            if (await db.Cards.AnyAsync(x => x.SizeId == id))
            {
                return Results.Conflict("Size is in use by cards.");
            }

            db.CardSizes.Remove(size);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(size.BoardId);
            return Results.NoContent();
        }).RequireAdmin();

        group.MapPatch("/sizes/{id:guid}", async (BoardDbContext db, Guid id, UpdateSizeRequest request, BoardEventBroadcaster broadcaster) =>
        {
            var size = await db.CardSizes.FindAsync(id);
            if (size is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                size.Name = request.Name;
            }

            if (request.Ordinal is not null)
            {
                var newOrd = request.Ordinal.Value;
                if (await db.CardSizes.AnyAsync(x => x.BoardId == size.BoardId && x.Ordinal == newOrd && x.Id != id))
                {
                    return Results.Conflict("Ordinal already taken by another size.");
                }

                size.Ordinal = newOrd;
            }

            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(size.BoardId);
            return Results.Ok(size);
        }).RequireAdmin();

        return group;
    }
}

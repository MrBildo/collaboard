using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class LabelEndpoints
{
    public static RouteGroupBuilder MapLabelEndpoints(this RouteGroupBuilder group)
    {
        // Board-scoped label CRUD
        group.MapGet("/boards/{boardId:guid}/labels", async (BoardDbContext db, Guid boardId) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            var labels = await db.Labels.Where(x => x.BoardId == boardId).OrderBy(x => x.Name).ToListAsync();
            return Results.Ok(labels);
        }).RequireAuth();

        group.MapPost("/boards/{boardId:guid}/labels", async (BoardDbContext db, Guid boardId, Label request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (await db.Labels.AnyAsync(x => x.BoardId == boardId && x.Name == request.Name))
            {
                return Results.Conflict("A label with that name already exists on this board.");
            }

            var label = new Label
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                Name = request.Name,
                Color = request.Color,
            };
            db.Labels.Add(label);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(boardId);
            return Results.Created($"/api/v1/boards/{boardId}/labels/{label.Id}", label);
        }).RequireAdmin();

        group.MapPatch("/boards/{boardId:guid}/labels/{id:guid}", async (BoardDbContext db, Guid boardId, Guid id, JsonElement patch, BoardEventBroadcaster broadcaster) =>
        {
            var label = await db.Labels.FindAsync(id);
            if (label is null || label.BoardId != boardId)
            {
                return Results.NotFound();
            }

            if (patch.TryGetProperty("name", out var name))
            {
                label.Name = name.GetString()!;
            }

            if (patch.TryGetProperty("color", out var color))
            {
                label.Color = color.ValueKind == JsonValueKind.Null ? null : color.GetString();
            }

            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(boardId);
            return Results.Ok(label);
        }).RequireAdmin();

        group.MapDelete("/boards/{boardId:guid}/labels/{id:guid}", async (BoardDbContext db, Guid boardId, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var label = await db.Labels.FindAsync(id);
            if (label is null || label.BoardId != boardId)
            {
                return Results.NotFound();
            }

            var cardLabels = await db.CardLabels.Where(x => x.LabelId == id).ToListAsync();
            db.CardLabels.RemoveRange(cardLabels);
            db.Labels.Remove(label);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(boardId);
            return Results.NoContent();
        }).RequireAdmin();

        // Card-label operations (card-scoped routes, unchanged)
        group.MapGet("/cards/{id:guid}/labels", async (BoardDbContext db, Guid id) =>
        {
            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            var labels = await db.CardLabels
                .Where(cl => cl.CardId == id)
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => l)
                .ToListAsync();

            return Results.Ok(labels);
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/labels", async (BoardDbContext db, Guid id, JsonElement body, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            var labelId = body.GetProperty("labelId").GetGuid();
            var label = await db.Labels.FindAsync(labelId);
            if (label is null)
            {
                return Results.NotFound();
            }

            // Validate that the label belongs to the same board as the card
            var cardBoardId = await db.Lanes.Where(l => l.Id == card.LaneId).Select(l => l.BoardId).FirstOrDefaultAsync();
            if (label.BoardId != cardBoardId)
            {
                return Results.BadRequest("Label does not belong to the same board as the card.");
            }

            if (await db.CardLabels.AnyAsync(x => x.CardId == id && x.LabelId == labelId))
            {
                return Results.Conflict("Label is already assigned to this card.");
            }

            var cardLabel = new CardLabel { CardId = id, LabelId = labelId };
            db.CardLabels.Add(cardLabel);
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(cardBoardId);
            return Results.Created($"/api/v1/cards/{id}/labels/{labelId}", cardLabel);
        }).RequireAuth();

        group.MapDelete("/cards/{id:guid}/labels/{labelId:guid}", async (BoardDbContext db, Guid id, Guid labelId, BoardEventBroadcaster broadcaster) =>
        {
            var cardLabel = await db.CardLabels.FindAsync(id, labelId);
            if (cardLabel is null)
            {
                return Results.NotFound();
            }

            db.CardLabels.Remove(cardLabel);
            await db.SaveChangesAsync();

            var boardId = await db.Cards
                .Where(c => c.Id == id)
                .Join(db.Lanes, c => c.LaneId, l => l.Id, (_, l) => l.BoardId)
                .FirstOrDefaultAsync();
            if (boardId != Guid.Empty)
            {
                broadcaster.PublishBoardUpdated(boardId);
            }

            return Results.NoContent();
        }).RequireAuth();

        return group;
    }
}

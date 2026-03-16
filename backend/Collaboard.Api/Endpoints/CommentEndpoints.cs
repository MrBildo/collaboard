using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CommentEndpoints
{
    public static RouteGroupBuilder MapCommentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cards/{id:guid}/comments", async (BoardDbContext db, Guid id) =>
        {
            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            var comments = (await db.Comments
                .Where(x => x.CardId == id)
                .ToListAsync())
                .OrderBy(x => x.LastUpdatedAtUtc)
                .ToList();
            return Results.Ok(comments);
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/comments", async (BoardDbContext db, HttpContext http, Guid id, CardComment request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            var comment = new CardComment
            {
                Id = Guid.NewGuid(),
                CardId = id,
                UserId = http.CurrentUser().Id,
                ContentMarkdown = request.ContentMarkdown,
                LastUpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Comments.Add(comment);
            await db.SaveChangesAsync();
            await db.PublishForCardAsync(id, broadcaster);
            return Results.Created($"/api/v1/cards/{id}/comments/{comment.Id}", comment);
        }).RequireAuth();

        group.MapDelete("/comments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var comment = await db.Comments.FindAsync(id);
            if (comment is null)
            {
                return Results.NotFound();
            }

            var user = http.CurrentUser();
            if (comment.UserId != user.Id && user.Role != UserRole.Administrator)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var cardId = comment.CardId;
            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            await db.PublishForCardAsync(cardId, broadcaster);
            return Results.NoContent();
        }).RequireAuth();

        group.MapPatch("/comments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch, BoardEventBroadcaster broadcaster) =>
        {
            var comment = await db.Comments.FindAsync(id);
            if (comment is null)
            {
                return Results.NotFound();
            }

            var user = http.CurrentUser();
            if (comment.UserId != user.Id && user.Role != UserRole.Administrator)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (patch.TryGetProperty("contentMarkdown", out var content))
            {
                comment.ContentMarkdown = content.GetString()!;
            }

            comment.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await db.PublishForCardAsync(comment.CardId, broadcaster);
            return Results.Ok(comment);
        }).RequireAuth();

        return group;
    }
}

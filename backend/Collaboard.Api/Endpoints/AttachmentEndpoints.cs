using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cards/{id:guid}/attachments", async (BoardDbContext db, Guid id) =>
        {
            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            var attachments = await db.Attachments
                .Where(x => x.CardId == id)
                .Select(x => new { x.Id, x.FileName, x.ContentType, x.AddedByUserId, x.AddedAtUtc })
                .ToListAsync();
            return Results.Ok(attachments);
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/attachments", async (BoardDbContext db, HttpContext http, Guid id, IFormFile file, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var attachment = new CardAttachment
            {
                Id = Guid.NewGuid(),
                CardId = id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Payload = ms.ToArray(),
                AddedByUserId = http.CurrentUser().Id,
                AddedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync();
            await db.PublishForCardAsync(id, broadcaster);
            return Results.Created($"/api/v1/cards/{id}/attachments/{attachment.Id}", new { attachment.Id, attachment.FileName });
        }).DisableAntiforgery().RequireAuth();

        group.MapGet("/attachments/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var attachment = await db.Attachments.FindAsync(id);
            return attachment is null ? Results.NotFound() : Results.File(attachment.Payload, attachment.ContentType, attachment.FileName);
        }).RequireAuth();

        group.MapDelete("/attachments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var attachment = await db.Attachments.FindAsync(id);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            var user = http.CurrentUser();
            if (attachment.AddedByUserId != user.Id && user.Role != UserRole.Administrator)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var cardId = attachment.CardId;
            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync();
            await db.PublishForCardAsync(cardId, broadcaster);
            return Results.NoContent();
        }).RequireAuth();

        return group;
    }
}

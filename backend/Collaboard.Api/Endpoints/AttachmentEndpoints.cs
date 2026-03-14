using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class AttachmentEndpoints
{
    public static RouteGroupBuilder MapAttachmentEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/cards/{id:guid}/attachments", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            if (!await db.Cards.AnyAsync(x => x.Id == id))
            {
                return Results.NotFound();
            }

            var attachments = await db.Attachments
                .Where(x => x.CardId == id)
                .Select(x => new { x.Id, x.FileName, x.ContentType, x.AddedByUserId, x.AddedAtUtc })
                .ToListAsync();
            return Results.Ok(attachments);
        });

        group.MapPost("/cards/{id:guid}/attachments", async (BoardDbContext db, HttpContext http, Guid id, IFormFile file) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

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
            return Results.Created($"/api/v1/cards/{id}/attachments/{attachment.Id}", new { attachment.Id, attachment.FileName });
        }).DisableAntiforgery();

        group.MapGet("/attachments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var attachment = await db.Attachments.FindAsync(id);
            return attachment is null ? Results.NotFound() : Results.File(attachment.Payload, attachment.ContentType, attachment.FileName);
        });

        group.MapDelete("/attachments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var attachment = await db.Attachments.FindAsync(id);
            if (attachment is null)
            {
                return Results.NotFound();
            }

            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return group;
    }
}

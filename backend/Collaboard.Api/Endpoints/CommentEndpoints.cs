using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CommentEndpoints
{
    public static RouteGroupBuilder MapCommentEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/cards/{id:guid}/comments", async (BoardDbContext db, HttpContext http, Guid id, CardComment request) =>
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
            return Results.Created($"/api/v1/cards/{id}/comments/{comment.Id}", comment);
        });

        group.MapDelete("/comments/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

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

            db.Comments.Remove(comment);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return group;
    }
}

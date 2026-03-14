using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardEndpoints
{
    public static RouteGroupBuilder MapCardEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/cards", async (BoardDbContext db, HttpContext http, CardItem request) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var now = DateTimeOffset.UtcNow;
            var currentUser = http.CurrentUser();
            var nextNumber = (await db.Cards.MaxAsync(x => (long?)x.Number) ?? 0) + 1;
            var card = new CardItem
            {
                Id = Guid.NewGuid(),
                Number = nextNumber,
                Name = request.Name,
                DescriptionMarkdown = request.DescriptionMarkdown,
                Status = request.Status,
                Size = request.Size,
                LaneId = request.LaneId,
                Position = request.Position,
                CreatedAtUtc = now,
                LastUpdatedAtUtc = now,
                CreatedByUserId = currentUser.Id,
                LastUpdatedByUserId = currentUser.Id,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/cards/{card.Id}", card);
        });

        group.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, CardItem patch) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (!string.IsNullOrEmpty(patch.Name))
            {
                card.Name = patch.Name;
            }

            if (!string.IsNullOrEmpty(patch.DescriptionMarkdown))
            {
                card.DescriptionMarkdown = patch.DescriptionMarkdown;
            }

            if (patch.Status is not null)
            {
                card.Status = patch.Status;
            }

            if (!string.IsNullOrEmpty(patch.Size))
            {
                card.Size = patch.Size;
            }

            if (patch.LaneId != Guid.Empty)
            {
                card.LaneId = patch.LaneId;
            }

            if (patch.Position != 0)
            {
                card.Position = patch.Position;
            }

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;
            await db.SaveChangesAsync();
            return Results.Ok(card);
        });

        group.MapDelete("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            db.Cards.Remove(card);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return group;
    }
}

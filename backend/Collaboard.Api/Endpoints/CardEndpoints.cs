using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardEndpoints
{
    private static readonly string[] _validSizes = ["S", "M", "L", "XL"];

    public static RouteGroupBuilder MapCardEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/cards", async (BoardDbContext db, HttpContext http, CardItem request) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            if (!string.IsNullOrEmpty(request.Size))
            {
                if (!_validSizes.Contains(request.Size))
                {
                    return Results.BadRequest("Size must be one of: S, M, L, XL");
                }
            }
            else
            {
                request.Size = "M";
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
                Blocked = request.Blocked,
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

        group.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch) =>
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

            if (patch.TryGetProperty("name", out var name))
            {
                card.Name = name.GetString()!;
            }

            if (patch.TryGetProperty("descriptionMarkdown", out var desc))
            {
                card.DescriptionMarkdown = desc.GetString()!;
            }

            if (patch.TryGetProperty("blocked", out var blocked))
            {
                card.Blocked = blocked.ValueKind == JsonValueKind.Null ? null : blocked.GetString();
            }

            if (patch.TryGetProperty("size", out var size))
            {
                var sizeVal = size.GetString()!;
                if (!_validSizes.Contains(sizeVal))
                {
                    return Results.BadRequest("Size must be one of: S, M, L, XL");
                }

                card.Size = sizeVal;
            }

            if (patch.TryGetProperty("laneId", out var lane))
            {
                card.LaneId = lane.GetGuid();
            }

            if (patch.TryGetProperty("position", out var pos))
            {
                card.Position = pos.GetInt32();
            }

            if (patch.TryGetProperty("labelIds", out var labelIds))
            {
                var existingLabels = await db.CardLabels.Where(x => x.CardId == id).ToListAsync();
                db.CardLabels.RemoveRange(existingLabels);
                foreach (var labelIdElement in labelIds.EnumerateArray())
                {
                    db.CardLabels.Add(new CardLabel { CardId = id, LabelId = labelIdElement.GetGuid() });
                }
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

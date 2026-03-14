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
        group.MapGet("/cards", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            return forbidden is not null ? forbidden : Results.Ok(await db.Cards.OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync());
        });

        group.MapGet("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var card = await db.Cards.FindAsync(id);
            return card is null ? Results.NotFound() : Results.Ok(card);
        });

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

        group.MapPost("/cards/{id:guid}/reorder", async (BoardDbContext db, HttpContext http, Guid id, JsonElement body) =>
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

            var targetLaneId = body.GetProperty("laneId").GetGuid();
            var targetIndex = body.GetProperty("index").GetInt32();

            if (!await db.Lanes.AnyAsync(l => l.Id == targetLaneId))
            {
                return Results.BadRequest("Lane not found.");
            }

            var sourceLaneId = card.LaneId;

            var targetCards = await db.Cards
                .Where(c => c.LaneId == targetLaneId && c.Id != id)
                .OrderBy(c => c.Position)
                .ToListAsync();

            targetIndex = Math.Clamp(targetIndex, 0, targetCards.Count);

            targetCards.Insert(targetIndex, card);

            for (var i = 0; i < targetCards.Count; i++)
            {
                targetCards[i].Position = i * 10;
            }

            if (sourceLaneId != targetLaneId)
            {
                card.LaneId = targetLaneId;

                var sourceCards = await db.Cards
                    .Where(c => c.LaneId == sourceLaneId && c.Id != id)
                    .OrderBy(c => c.Position)
                    .ToListAsync();

                for (var i = 0; i < sourceCards.Count; i++)
                {
                    sourceCards[i].Position = i * 10;
                }
            }

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;

            await db.SaveChangesAsync();

            var lanes = await db.Lanes.OrderBy(l => l.Position).ToListAsync();
            var cards = await db.Cards.OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync();
            return Results.Ok(new { lanes, cards });
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

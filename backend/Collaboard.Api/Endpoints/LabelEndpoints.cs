using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class LabelEndpoints
{
    public static RouteGroupBuilder MapLabelEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/labels", async (BoardDbContext db, HttpContext http) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var labels = await db.Labels.ToListAsync();
            return Results.Ok(labels);
        });

        group.MapPost("/labels", async (BoardDbContext db, HttpContext http, Label request) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            if (await db.Labels.AnyAsync(x => x.Name == request.Name))
            {
                return Results.Conflict("A label with that name already exists.");
            }

            var label = new Label
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Color = request.Color,
            };
            db.Labels.Add(label);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/labels/{label.Id}", label);
        });

        group.MapPatch("/labels/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var label = await db.Labels.FindAsync(id);
            if (label is null)
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
            return Results.Ok(label);
        });

        group.MapDelete("/labels/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var label = await db.Labels.FindAsync(id);
            if (label is null)
            {
                return Results.NotFound();
            }

            var cardLabels = await db.CardLabels.Where(x => x.LabelId == id).ToListAsync();
            db.CardLabels.RemoveRange(cardLabels);
            db.Labels.Remove(label);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapGet("/cards/{id:guid}/labels", async (BoardDbContext db, HttpContext http, Guid id) =>
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

            var labels = await db.CardLabels
                .Where(cl => cl.CardId == id)
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => l)
                .ToListAsync();

            return Results.Ok(labels);
        });

        group.MapPost("/cards/{id:guid}/labels", async (BoardDbContext db, HttpContext http, Guid id, JsonElement body) =>
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

            var labelId = body.GetProperty("labelId").GetGuid();
            if (!await db.Labels.AnyAsync(x => x.Id == labelId))
            {
                return Results.NotFound();
            }

            if (await db.CardLabels.AnyAsync(x => x.CardId == id && x.LabelId == labelId))
            {
                return Results.Conflict("Label is already assigned to this card.");
            }

            var cardLabel = new CardLabel { CardId = id, LabelId = labelId };
            db.CardLabels.Add(cardLabel);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/cards/{id}/labels/{labelId}", cardLabel);
        });

        group.MapDelete("/cards/{id:guid}/labels/{labelId:guid}", async (BoardDbContext db, HttpContext http, Guid id, Guid labelId) =>
        {
            var forbidden = await http.RequireRoleAsync(db, UserRole.Administrator, UserRole.AgentUser, UserRole.HumanUser);
            if (forbidden is not null)
            {
                return forbidden;
            }

            var cardLabel = await db.CardLabels.FindAsync(id, labelId);
            if (cardLabel is null)
            {
                return Results.NotFound();
            }

            db.CardLabels.Remove(cardLabel);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return group;
    }
}

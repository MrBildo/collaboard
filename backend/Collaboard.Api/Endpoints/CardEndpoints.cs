using System.Text.Json;
using Collaboard.Api.Auth;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Endpoints;

internal static class CardEndpoints
{
    public static RouteGroupBuilder MapCardEndpoints(this RouteGroupBuilder group)
    {
        // Board-scoped listing and creation
        group.MapGet("/boards/{boardId:guid}/cards", async (BoardDbContext db, Guid boardId, DateTimeOffset? since, Guid? labelId, Guid? laneId, string? search) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            var query = db.Cards.Where(x => x.BoardId == boardId);

            if (laneId.HasValue)
            {
                query = query.Where(x => x.LaneId == laneId.Value);
            }

            if (since.HasValue)
            {
                query = CardQueryHelper.ApplySinceFilter(query, db, since.Value);
            }

            if (labelId.HasValue)
            {
                var cardIdsWithLabel = db.CardLabels.Where(cl => cl.LabelId == labelId.Value).Select(cl => cl.CardId);
                query = query.Where(x => cardIdsWithLabel.Contains(x.Id));
            }

            query = SearchHelper.ApplySearchFilter(query, search);

            var cards = await query.OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();
            var summaries = await CardSummaryBuilder.BuildAsync(db, cards);
            return Results.Ok(summaries);
        }).RequireAuth();

        group.MapPost("/boards/{boardId:guid}/cards", async (BoardDbContext db, HttpContext http, Guid boardId, JsonElement body, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (!body.TryGetProperty("laneId", out var laneIdProp) || laneIdProp.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest("laneId is required.");
            }

            var requestLaneId = laneIdProp.GetGuid();

            if (!body.TryGetProperty("name", out var nameProp) || string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                return Results.BadRequest("Name is required.");
            }

            var requestName = nameProp.GetString()!;

            var requestDescription = body.TryGetProperty("descriptionMarkdown", out var descProp) ? descProp.GetString() ?? "" : "";
            var requestPosition = body.TryGetProperty("position", out var posProp) ? posProp.GetInt32() : 0;

            // Verify the lane belongs to this board
            if (!await db.Lanes.AnyAsync(x => x.Id == requestLaneId && x.BoardId == boardId))
            {
                return Results.BadRequest("Lane does not belong to this board.");
            }

            Guid sizeId;
            if (body.TryGetProperty("sizeId", out var sizeIdProp) && sizeIdProp.ValueKind != JsonValueKind.Null)
            {
                sizeId = sizeIdProp.GetGuid();
                if (!await db.CardSizes.AnyAsync(s => s.Id == sizeId && s.BoardId == boardId))
                {
                    return Results.BadRequest("Size does not belong to this board.");
                }
            }
            else
            {
                var defaultSize = await db.CardSizes
                    .Where(s => s.BoardId == boardId)
                    .OrderBy(s => s.Ordinal)
                    .FirstOrDefaultAsync();
                if (defaultSize is null)
                {
                    return Results.BadRequest("Board has no sizes configured.");
                }

                sizeId = defaultSize.Id;
            }

            var now = DateTimeOffset.UtcNow;
            var currentUser = http.CurrentUser();
            var card = new CardItem
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                Name = requestName,
                DescriptionMarkdown = requestDescription,
                SizeId = sizeId,
                LaneId = requestLaneId,
                Position = requestPosition,
                CreatedAtUtc = now,
                LastUpdatedAtUtc = now,
                CreatedByUserId = currentUser.Id,
                LastUpdatedByUserId = currentUser.Id,
            };
            await CardNumberHelper.InsertCardWithAutoNumberAsync(db, card, boardId);
            broadcaster.PublishBoardUpdated(boardId);
            return Results.Created($"/api/v1/cards/{card.Id}", card);
        }).RequireAuth();

        // By-ID operations (flat)
        group.MapGet("/cards/{id:guid}", async (BoardDbContext db, Guid id) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            var detail = await CardDetailBuilder.BuildAsync(db, card);
            return Results.Ok(detail);
        }).RequireAuth();

        group.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, JsonElement patch, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (patch.TryGetProperty("name", out var name))
            {
                var nameStr = name.ValueKind == JsonValueKind.Null ? null : name.GetString();
                if (string.IsNullOrWhiteSpace(nameStr))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                card.Name = nameStr;
            }

            if (patch.TryGetProperty("descriptionMarkdown", out var desc))
            {
                card.DescriptionMarkdown = desc.ValueKind == JsonValueKind.Null ? "" : desc.GetString()!;
            }

            if (patch.TryGetProperty("sizeId", out var sizeIdPatch))
            {
                if (sizeIdPatch.ValueKind == JsonValueKind.Null)
                {
                    return Results.BadRequest("sizeId cannot be null.");
                }

                var newSizeId = sizeIdPatch.GetGuid();
                var sizeLane = await db.Lanes.FindAsync(card.LaneId);
                if (sizeLane is null || !await db.CardSizes.AnyAsync(s => s.Id == newSizeId && s.BoardId == sizeLane.BoardId))
                {
                    return Results.BadRequest("Size does not belong to this board.");
                }

                card.SizeId = newSizeId;
            }

            if (patch.TryGetProperty("laneId", out var lane))
            {
                if (lane.ValueKind == JsonValueKind.Null)
                {
                    return Results.BadRequest("laneId cannot be null.");
                }

                var newLaneId = lane.GetGuid();
                var targetLane = await db.Lanes.FindAsync(newLaneId);
                if (targetLane is null)
                {
                    return Results.BadRequest("Lane not found.");
                }

                if (targetLane.BoardId != card.BoardId)
                {
                    return Results.BadRequest("Lane does not belong to this board.");
                }

                card.LaneId = newLaneId;
            }

            if (patch.TryGetProperty("position", out var pos))
            {
                card.Position = pos.GetInt32();
            }

            if (patch.TryGetProperty("labelIds", out var labelIds))
            {
                var newLabelIds = labelIds.EnumerateArray().Select(e => e.GetGuid()).ToList();
                if (newLabelIds.Count > 0)
                {
                    var validCount = await db.Labels.CountAsync(l => newLabelIds.Contains(l.Id) && l.BoardId == card.BoardId);
                    if (validCount != newLabelIds.Count)
                    {
                        return Results.BadRequest("One or more labels do not belong to this board.");
                    }
                }

                var existingLabels = await db.CardLabels.Where(x => x.CardId == id).ToListAsync();
                db.CardLabels.RemoveRange(existingLabels);
                foreach (var labelId in newLabelIds)
                {
                    db.CardLabels.Add(new CardLabel { CardId = id, LabelId = labelId });
                }
            }

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;
            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(card.BoardId);

            return Results.Ok(card);
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/reorder", async (BoardDbContext db, HttpContext http, Guid id, JsonElement body, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (!body.TryGetProperty("laneId", out var laneIdProp) || laneIdProp.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest("laneId is required.");
            }

            var targetLaneId = laneIdProp.GetGuid();

            if (!body.TryGetProperty("index", out var indexProp) || indexProp.ValueKind == JsonValueKind.Null)
            {
                return Results.BadRequest("index is required.");
            }

            var targetIndex = indexProp.GetInt32();

            var targetLane = await db.Lanes.FindAsync(targetLaneId);
            if (targetLane is null)
            {
                return Results.BadRequest("Lane not found.");
            }

            var sourceLane = await db.Lanes.FindAsync(card.LaneId);
            if (sourceLane is null || sourceLane.BoardId != targetLane.BoardId)
            {
                return Results.BadRequest("Cannot move cards between boards.");
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, targetLaneId, targetIndex);

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;

            await db.SaveChangesAsync();
            broadcaster.PublishBoardUpdated(targetLane.BoardId);

            var boardLaneIds = await db.Lanes.Where(x => x.BoardId == targetLane.BoardId).Select(x => x.Id).ToListAsync();
            var lanes = await db.Lanes.Where(x => x.BoardId == targetLane.BoardId).OrderBy(l => l.Position).ToListAsync();
            var cards = await db.Cards.Where(x => boardLaneIds.Contains(x.LaneId)).OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync();
            return Results.Ok(new { lanes, cards });
        }).RequireAuth();

        group.MapDelete("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            var user = http.CurrentUser();
            if (user.Role != UserRole.Administrator && card.CreatedByUserId != user.Id)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var lane = await db.Lanes.FindAsync(card.LaneId);

            db.Cards.Remove(card);
            await db.SaveChangesAsync();

            if (lane is not null)
            {
                broadcaster.PublishBoardUpdated(lane.BoardId);
            }

            return Results.NoContent();
        }).RequireRole(UserRole.Administrator, UserRole.HumanUser);

        return group;
    }
}

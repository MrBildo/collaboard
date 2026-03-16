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
        group.MapGet("/boards/{boardId:guid}/cards", async (BoardDbContext db, Guid boardId, DateTimeOffset? since, Guid? labelId, Guid? laneId) =>
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
                var cardIdsWithRecentComments = db.Comments
                    .Where(c => c.LastUpdatedAtUtc >= since.Value)
                    .Select(c => c.CardId);
                var cardIdsWithRecentAttachments = db.Attachments
                    .Where(a => a.AddedAtUtc >= since.Value)
                    .Select(a => a.CardId);

                query = query.Where(x =>
                    x.CreatedAtUtc >= since.Value
                    || x.LastUpdatedAtUtc >= since.Value
                    || cardIdsWithRecentComments.Contains(x.Id)
                    || cardIdsWithRecentAttachments.Contains(x.Id));
            }

            if (labelId.HasValue)
            {
                var cardIdsWithLabel = db.CardLabels.Where(cl => cl.LabelId == labelId.Value).Select(cl => cl.CardId);
                query = query.Where(x => cardIdsWithLabel.Contains(x.Id));
            }

            var cards = await query.OrderBy(x => x.LaneId).ThenBy(x => x.Position).ToListAsync();

            if (cards.Count == 0)
            {
                return Results.Ok(Array.Empty<CardSummary>());
            }

            var cardIds = cards.Select(c => c.Id).ToList();

            // Batch load sizes
            var sizeIds = cards.Select(c => c.SizeId).Distinct().ToList();
            var sizeNames = await db.CardSizes
                .Where(s => sizeIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name);

            // Batch load labels
            var cardLabels = await db.CardLabels
                .Where(cl => cardIds.Contains(cl.CardId))
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => new { cl.CardId, Label = new CardLabelSummary(l.Id, l.Name, l.Color) })
                .ToListAsync();
            var labelsByCard = cardLabels.GroupBy(x => x.CardId).ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());

            // Batch load counts
            var commentCounts = await db.Comments
                .Where(cm => cardIds.Contains(cm.CardId))
                .GroupBy(cm => cm.CardId)
                .Select(g => new { CardId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CardId, x => x.Count);

            var attachmentCounts = await db.Attachments
                .Where(a => cardIds.Contains(a.CardId))
                .GroupBy(a => a.CardId)
                .Select(g => new { CardId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CardId, x => x.Count);

            // Project
            var summaries = cards.Select(c => new CardSummary(
                c.Id, c.Number, c.Name, c.DescriptionMarkdown,
                c.SizeId, sizeNames.GetValueOrDefault(c.SizeId, "?"),
                c.LaneId, c.Position,
                c.CreatedByUserId, c.CreatedAtUtc,
                c.LastUpdatedByUserId, c.LastUpdatedAtUtc,
                labelsByCard.GetValueOrDefault(c.Id, []),
                commentCounts.GetValueOrDefault(c.Id, 0),
                attachmentCounts.GetValueOrDefault(c.Id, 0)
            )).ToList();

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

            var comments = (await db.Comments
                .Where(c => c.CardId == id)
                .ToListAsync())
                .OrderBy(c => c.LastUpdatedAtUtc)
                .ToList();

            var userIds = comments.Select(c => c.UserId)
                .Append(card.CreatedByUserId)
                .Append(card.LastUpdatedByUserId)
                .Distinct()
                .ToList();
            var userNames = await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var commentsWithUserNames = comments.Select(c => new
            {
                c.Id,
                c.CardId,
                c.UserId,
                userName = userNames.GetValueOrDefault(c.UserId),
                c.ContentMarkdown,
                c.LastUpdatedAtUtc,
            });

            var labels = await db.CardLabels.Where(cl => cl.CardId == id)
                .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => l)
                .ToListAsync();

            var attachments = await db.Attachments
                .Where(a => a.CardId == id)
                .Select(a => new { a.Id, a.FileName, a.ContentType, a.AddedByUserId, a.AddedAtUtc })
                .ToListAsync();

            var sizeName = await db.CardSizes.Where(s => s.Id == card.SizeId).Select(s => s.Name).FirstOrDefaultAsync() ?? "?";

            return Results.Ok(new
            {
                card,
                sizeName,
                createdByUserName = userNames.GetValueOrDefault(card.CreatedByUserId),
                lastUpdatedByUserName = userNames.GetValueOrDefault(card.LastUpdatedByUserId),
                comments = commentsWithUserNames,
                labels,
                attachments,
            });
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
                if (!await db.Lanes.AnyAsync(l => l.Id == newLaneId))
                {
                    return Results.BadRequest("Lane not found.");
                }

                card.LaneId = newLaneId;
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

            // Resolve the board from the card's lane
            var cardLane = await db.Lanes.FindAsync(card.LaneId);
            if (cardLane is not null)
            {
                broadcaster.PublishBoardUpdated(cardLane.BoardId);
            }

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

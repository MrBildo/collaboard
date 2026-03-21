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
        group.MapGet("/boards/{boardId:guid}/cards", async (BoardDbContext db, Guid boardId, DateTimeOffset? since, Guid? labelId, Guid? laneId, string? search, int? skip, int? take) =>
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

            var orderedQuery = query.OrderBy(x => x.LaneId).ThenBy(x => x.Position);

            // Two queries: COUNT then Skip/Take. The count re-executes filter predicates
            // (including since subqueries). Acceptable at current scale; revisit if perf degrades.
            var totalCount = await query.CountAsync();

            var effectiveSkip = Math.Max(skip ?? 0, 0);
            int? effectiveTake = take.HasValue ? Math.Clamp(take.Value, 1, 200) : null;

            var pagedQuery = orderedQuery.Skip(effectiveSkip);
            if (effectiveTake.HasValue)
            {
                pagedQuery = pagedQuery.Take(effectiveTake.Value);
            }

            var cards = await pagedQuery.ToListAsync();
            var summaries = await CardSummaryBuilder.BuildAsync(db, cards);
            return Results.Ok(new PagedResult<CardSummary>(summaries, totalCount, effectiveSkip, effectiveTake));
        }).RequireAuth();

        group.MapPost("/boards/{boardId:guid}/cards", async (BoardDbContext db, HttpContext http, Guid boardId, CreateCardRequest request, BoardEventBroadcaster broadcaster) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId))
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            var requestDescription = request.DescriptionMarkdown ?? "";

            // Verify the lane belongs to this board
            if (!await db.Lanes.AnyAsync(x => x.Id == request.LaneId && x.BoardId == boardId))
            {
                return Results.BadRequest("Lane does not belong to this board.");
            }

            Guid sizeId;
            if (request.SizeId is not null)
            {
                sizeId = request.SizeId.Value;
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

            int requestPosition;
            if (request.Position.HasValue)
            {
                requestPosition = request.Position.Value;
            }
            else
            {
                var maxPosition = await db.Cards.Where(c => c.LaneId == request.LaneId).MaxAsync(c => (int?)c.Position) ?? -10;
                requestPosition = maxPosition + 10;
            }

            var now = DateTimeOffset.UtcNow;
            var currentUser = http.CurrentUser();
            var card = new CardItem
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                Name = request.Name,
                DescriptionMarkdown = requestDescription,
                SizeId = sizeId,
                LaneId = request.LaneId,
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

        group.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, UpdateCardRequest request, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest("Name cannot be empty.");
                }

                card.Name = request.Name;
            }

            if (request.DescriptionMarkdown is not null)
            {
                card.DescriptionMarkdown = request.DescriptionMarkdown;
            }

            if (request.SizeId is not null)
            {
                var newSizeId = request.SizeId.Value;
                var sizeLane = await db.Lanes.FindAsync(card.LaneId);
                if (sizeLane is null || !await db.CardSizes.AnyAsync(s => s.Id == newSizeId && s.BoardId == sizeLane.BoardId))
                {
                    return Results.BadRequest("Size does not belong to this board.");
                }

                card.SizeId = newSizeId;
            }

            if (request.LaneId is not null)
            {
                var newLaneId = request.LaneId.Value;
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

                if (request.Position is null)
                {
                    var maxPosition = await db.Cards.Where(c => c.LaneId == newLaneId && c.Id != id).MaxAsync(c => (int?)c.Position) ?? -10;
                    card.Position = maxPosition + 10;
                }
            }

            if (request.Position is not null)
            {
                card.Position = request.Position.Value;
            }

            if (request.LabelIds is not null)
            {
                if (request.LabelIds.Length > 0)
                {
                    var validCount = await db.Labels.CountAsync(l => request.LabelIds.Contains(l.Id) && l.BoardId == card.BoardId);
                    if (validCount != request.LabelIds.Length)
                    {
                        return Results.BadRequest("One or more labels do not belong to this board.");
                    }
                }

                var existingLabels = await db.CardLabels.Where(x => x.CardId == id).ToListAsync();
                db.CardLabels.RemoveRange(existingLabels);
                foreach (var labelId in request.LabelIds)
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

        group.MapPost("/cards/{id:guid}/reorder", async (BoardDbContext db, HttpContext http, Guid id, ReorderCardRequest request, BoardEventBroadcaster broadcaster) =>
        {
            var card = await db.Cards.FindAsync(id);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (request.LaneId is null)
            {
                return Results.BadRequest("laneId is required.");
            }

            var targetLaneId = request.LaneId.Value;

            if (request.Index is null)
            {
                return Results.BadRequest("index is required.");
            }

            var targetIndex = request.Index.Value;

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

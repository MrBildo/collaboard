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
        group.MapGet("/boards/{boardId:guid}/cards", async (BoardDbContext db, Guid boardId, DateTimeOffset? since, Guid? labelId, Guid? laneId, string? search, bool? includeArchived, int? offset, int? limit, CancellationToken ct) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId, ct))
            {
                return Results.NotFound();
            }

            var query = CardQueryHelper.BoardCards(db.Cards, db.Lanes, boardId, includeArchived is true);

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

            var orderedQuery = CardQueryHelper.OrderForBoard(query);

            // Two queries: COUNT then offset/limit. The count re-executes filter predicates
            // (including since subqueries). Acceptable at current scale; revisit if perf degrades.
            var totalCount = await query.CountAsync(ct);

            var effectiveOffset = Math.Max(offset ?? 0, 0);
            int? effectiveLimit = limit.HasValue ? Math.Clamp(limit.Value, 1, 200) : null;

            var pagedQuery = orderedQuery.Skip(effectiveOffset);
            if (effectiveLimit.HasValue)
            {
                pagedQuery = pagedQuery.Take(effectiveLimit.Value);
            }

            var cards = await pagedQuery.ToListAsync(ct);
            var summaries = await CardSummaryBuilder.BuildAsync(db, cards, ct);
            return Results.Ok(new PagedResult<CardSummary>(summaries, totalCount, effectiveOffset, effectiveLimit));
        }).RequireAuth();

        group.MapPost("/boards/{boardId:guid}/cards", async (BoardDbContext db, HttpContext http, Guid boardId, CreateCardRequest request, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId, ct))
            {
                return Results.NotFound();
            }

            var (card, error) = await CardCreateHelper.BuildCardAsync(db, boardId, request, http.CurrentUser());
            if (error is not null)
            {
                return error;
            }

            await CardNumberHelper.InsertCardWithAutoNumberAsync(db, card!, boardId, ct);
            broadcaster.PublishBoardUpdated(boardId);

            var summaries = await CardSummaryBuilder.BuildAsync(db, [card!], ct);
            return Results.Created($"/api/v1/cards/{card!.Id}", summaries[0]);
        }).RequireAuth();

        // By-ID operations (flat)
        group.MapGet("/cards/{id:guid}", async (BoardDbContext db, Guid id, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            var detail = await CardDetailBuilder.BuildAsync(db, card, ct);
            return Results.Ok(detail);
        }).RequireAuth();

        group.MapPatch("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, UpdateCardRequest request, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (await ArchiveGuard.IsCardArchivedAsync(db, id))
            {
                return Results.BadRequest("Archived cards cannot be modified. Restore the card first.");
            }

            if (card.IsTemp)
            {
                return Results.BadRequest("Temp cards cannot be modified via this endpoint.");
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
                var sizeLane = await db.Lanes.FindAsync([card.LaneId], ct);
                if (sizeLane is null || !await db.CardSizes.AnyAsync(s => s.Id == newSizeId && s.BoardId == sizeLane.BoardId, ct))
                {
                    return Results.BadRequest("Size does not belong to this board.");
                }

                card.SizeId = newSizeId;
            }

            if (request.LaneId is not null)
            {
                var newLaneId = request.LaneId.Value;
                var targetLane = await db.Lanes.FindAsync([newLaneId], ct);
                if (targetLane is null)
                {
                    return Results.BadRequest("Lane not found.");
                }

                if (targetLane.BoardId != card.BoardId)
                {
                    return Results.BadRequest("Lane does not belong to this board.");
                }

                if (targetLane.IsArchiveLane)
                {
                    return Results.BadRequest("Use the archive endpoint to archive cards.");
                }

                card.LaneId = newLaneId;

                if (request.Position is null)
                {
                    var maxPosition = await db.Cards.Where(c => c.LaneId == newLaneId && c.Id != id).MaxAsync(c => (int?)c.Position, ct) ?? -10;
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
                    var validCount = await db.Labels.CountAsync(l => request.LabelIds.Contains(l.Id) && l.BoardId == card.BoardId, ct);
                    if (validCount != request.LabelIds.Length)
                    {
                        return Results.BadRequest("One or more labels do not belong to this board.");
                    }
                }

                var existingLabels = await db.CardLabels.Where(x => x.CardId == id).ToListAsync(ct);
                db.CardLabels.RemoveRange(existingLabels);
                foreach (var labelId in request.LabelIds)
                {
                    db.CardLabels.Add(new CardLabel { CardId = id, LabelId = labelId });
                }
            }

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;
            await db.SaveChangesAsync(ct);
            broadcaster.PublishBoardUpdated(card.BoardId);

            var summaries = await CardSummaryBuilder.BuildAsync(db, [card], ct);
            return Results.Ok(summaries[0]);
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/reorder", async (BoardDbContext db, HttpContext http, Guid id, ReorderCardRequest request, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (await ArchiveGuard.IsCardArchivedAsync(db, id))
            {
                return Results.BadRequest("Archived cards cannot be modified. Restore the card first.");
            }

            if (await TempGuard.IsCardTempAsync(db, id))
            {
                return Results.BadRequest("Temp cards cannot be reordered.");
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

            var targetLane = await db.Lanes.FindAsync([targetLaneId], ct);
            if (targetLane is null)
            {
                return Results.BadRequest("Lane not found.");
            }

            if (targetLane.IsArchiveLane)
            {
                return Results.BadRequest("Use archive_card to archive cards.");
            }

            var sourceLane = await db.Lanes.FindAsync([card.LaneId], ct);
            if (sourceLane is null || sourceLane.BoardId != targetLane.BoardId)
            {
                return Results.BadRequest("Cannot move cards between boards.");
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, targetLaneId, targetIndex, ct);

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;

            await db.SaveChangesAsync(ct);
            broadcaster.PublishBoardUpdated(targetLane.BoardId);

            var boardLaneIds = await db.Lanes.Where(x => x.BoardId == targetLane.BoardId).Select(x => x.Id).ToListAsync(ct);
            var lanes = await db.Lanes.Where(x => x.BoardId == targetLane.BoardId).OrderBy(l => l.Position).ToListAsync(ct);
            var cards = await db.Cards.Where(x => boardLaneIds.Contains(x.LaneId)).OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync(ct);
            return Results.Ok(new { lanes, cards });
        }).RequireAuth();

        group.MapDelete("/cards/{id:guid}", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            var user = http.CurrentUser();
            if (user.Role != UserRole.Administrator && card.CreatedByUserId != user.Id)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var lane = await db.Lanes.FindAsync([card.LaneId], ct);

            db.Cards.Remove(card);
            await db.SaveChangesAsync(ct);

            if (lane is not null)
            {
                broadcaster.PublishBoardUpdated(lane.BoardId);
            }

            return Results.NoContent();
        }).RequireRole(UserRole.Administrator, UserRole.HumanUser);

        group.MapPost("/cards/{id:guid}/archive", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (card.IsTemp)
            {
                return Results.BadRequest("Temp cards cannot be archived.");
            }

            var currentLane = await db.Lanes.FindAsync([card.LaneId], ct);
            if (currentLane is not null && currentLane.IsArchiveLane)
            {
                return Results.BadRequest("Card is already archived.");
            }

            var archiveLane = await db.Lanes.FirstOrDefaultAsync(l => l.BoardId == card.BoardId && l.IsArchiveLane, ct);
            if (archiveLane is null)
            {
                return Results.BadRequest("Board has no archive lane.");
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, archiveLane.Id, 0, ct);

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;

            await db.SaveChangesAsync(ct);
            broadcaster.PublishBoardUpdated(card.BoardId);

            return Results.NoContent();
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/restore", async (BoardDbContext db, HttpContext http, Guid id, RestoreCardRequest request, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            var currentLane = await db.Lanes.FindAsync([card.LaneId], ct);
            if (currentLane is null || !currentLane.IsArchiveLane)
            {
                return Results.BadRequest("Card is not archived.");
            }

            var targetLane = await db.Lanes.FindAsync([request.LaneId], ct);
            if (targetLane is null)
            {
                return Results.NotFound();
            }

            if (targetLane.IsArchiveLane)
            {
                return Results.BadRequest("Cannot restore to an archive lane.");
            }

            if (targetLane.BoardId != card.BoardId)
            {
                return Results.BadRequest("Lane does not belong to this board.");
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, targetLane.Id, 0, ct);

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;

            await db.SaveChangesAsync(ct);
            broadcaster.PublishBoardUpdated(card.BoardId);

            return Results.NoContent();
        }).RequireAuth();

        // Temp card lifecycle endpoints
        group.MapPost("/boards/{boardId:guid}/cards/temp", async (BoardDbContext db, HttpContext http, Guid boardId, CreateCardRequest request, CancellationToken ct) =>
        {
            if (!await db.Boards.AnyAsync(x => x.Id == boardId, ct))
            {
                return Results.NotFound();
            }

            var (card, error) = await CardCreateHelper.BuildCardAsync(db, boardId, request, http.CurrentUser());
            if (error is not null)
            {
                return error;
            }

            card!.Number = 0;
            card.IsTemp = true;
            db.Cards.Add(card);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/cards/{card.Id}", new { card.Id });
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/finalize", async (BoardDbContext db, HttpContext http, Guid id, BoardEventBroadcaster broadcaster, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (!card.IsTemp)
            {
                return Results.BadRequest("Card is not a temp card.");
            }

            if (http.CurrentUser().Id != card.CreatedByUserId)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
            card.LastUpdatedByUserId = http.CurrentUser().Id;
            try
            {
                await CardNumberHelper.FinalizeCardNumberAsync(db, card, card.BoardId, ct);
            }
            catch (InvalidOperationException)
            {
                return Results.StatusCode(500);
            }

            broadcaster.PublishBoardUpdated(card.BoardId);
            return Results.Ok(new { card.Id, card.Number });
        }).RequireAuth();

        group.MapPost("/cards/{id:guid}/cancel", async (BoardDbContext db, HttpContext http, Guid id, CancellationToken ct) =>
        {
            var card = await db.Cards.FindAsync([id], ct);
            if (card is null)
            {
                return Results.NotFound();
            }

            if (!card.IsTemp)
            {
                return Results.BadRequest("Card is not a temp card.");
            }

            if (http.CurrentUser().Id != card.CreatedByUserId)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            db.Cards.Remove(card);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuth();

        return group;
    }
}

// Shared card-construction logic for the standard create and temp-create paths.
// Both paths take the same CreateCardRequest and perform identical validation,
// size resolution, position calculation, and label staging. They differ only in
// how the card is persisted (number allocation via CardNumberHelper vs. plain Add
// with IsTemp = true). BuildCardAsync encapsulates the shared portion; the caller
// sets Number / IsTemp and saves.
file static class CardCreateHelper
{
    public static async Task<(CardItem? Card, IResult? Error)> BuildCardAsync(
        BoardDbContext db,
        Guid boardId,
        CreateCardRequest request,
        BoardUser currentUser)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (null, Results.BadRequest("Name is required."));
        }

        var targetLane = await db.Lanes.FirstOrDefaultAsync(x => x.Id == request.LaneId && x.BoardId == boardId);
        if (targetLane is null)
        {
            return (null, Results.BadRequest("Lane does not belong to this board."));
        }

        if (targetLane.IsArchiveLane)
        {
            return (null, Results.BadRequest("Cards cannot be created in the archive lane."));
        }

        Guid sizeId;
        if (request.SizeId is not null)
        {
            sizeId = request.SizeId.Value;
            if (!await db.CardSizes.AnyAsync(s => s.Id == sizeId && s.BoardId == boardId))
            {
                return (null, Results.BadRequest("Size does not belong to this board."));
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
                return (null, Results.BadRequest("Board has no sizes configured."));
            }

            sizeId = defaultSize.Id;
        }

        int position;
        if (request.Position.HasValue)
        {
            position = request.Position.Value;
        }
        else
        {
            var maxPosition = await db.Cards
                .Where(c => c.LaneId == request.LaneId)
                .MaxAsync(c => (int?)c.Position) ?? -10;
            position = maxPosition + 10;
        }

        var now = DateTimeOffset.UtcNow;
        var card = new CardItem
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            Name = request.Name,
            DescriptionMarkdown = request.DescriptionMarkdown ?? "",
            SizeId = sizeId,
            LaneId = request.LaneId,
            Position = position,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now,
            CreatedByUserId = currentUser.Id,
            LastUpdatedByUserId = currentUser.Id,
        };

        if (request.LabelIds is not null && request.LabelIds.Length > 0)
        {
            var validCount = await db.Labels.CountAsync(l => request.LabelIds.Contains(l.Id) && l.BoardId == boardId);
            if (validCount != request.LabelIds.Length)
            {
                return (null, Results.BadRequest("One or more labels do not belong to this board."));
            }

            foreach (var labelId in request.LabelIds)
            {
                db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = labelId });
            }
        }

        return (card, null);
    }
}

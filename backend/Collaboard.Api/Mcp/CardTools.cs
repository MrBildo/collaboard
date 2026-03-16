using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class CardTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "create_card", Destructive = false)]
    [Description("Create a new card on the kanban board.")]
    public async Task<string> CreateCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The title/name of the card")] string name,
        [Description("The ID (guid) of the lane to place the card in")] Guid laneId,
        [Description("Optional markdown description")] string? descriptionMarkdown = null,
        [Description("Optional size ID (guid). If omitted, uses the board's lowest-ordinal size.")] Guid? sizeId = null,
        [Description("Optional size name (e.g. 'M', 'XL'). Used if sizeId is not provided.")] string? sizeName = null,
        [Description("Optional comma-separated label IDs (guids) to assign to the card at creation. All labels must belong to the same board as the lane.")] string? labelIds = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var lane = await db.Lanes.FirstOrDefaultAsync(l => l.Id == laneId, ct);
        if (lane is null)
        {
            return "Error: Lane not found.";
        }

        // Resolve size
        var (resolvedSizeId, sizeError) = await ResolveSizeAsync(lane.BoardId, sizeId, sizeName, ct);
        if (sizeError is not null)
        {
            return sizeError;
        }

        // Parse and validate label IDs if provided
        var (parsedLabelIds, labelError) = await ParseAndValidateLabelIdsAsync(labelIds, lane.BoardId, ct);
        if (labelError is not null)
        {
            return labelError;
        }

        var minPosition = await db.Cards.Where(c => c.LaneId == laneId).MinAsync(c => (int?)c.Position, ct) ?? 10;

        var now = DateTimeOffset.UtcNow;
        var card = new CardItem
        {
            Id = Guid.NewGuid(),
            BoardId = lane.BoardId,
            Name = name,
            DescriptionMarkdown = descriptionMarkdown ?? string.Empty,
            SizeId = resolvedSizeId!.Value,
            LaneId = laneId,
            Position = minPosition - 10,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now,
            CreatedByUserId = user!.Id,
            LastUpdatedByUserId = user.Id,
        };
        await CardNumberHelper.InsertCardWithAutoNumberAsync(db, card, lane.BoardId, ct);

        if (parsedLabelIds.Count > 0)
        {
            foreach (var labelId in parsedLabelIds)
            {
                db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = labelId });
            }

            await db.SaveChangesAsync(ct);
        }

        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "move_card", Destructive = false)]
    [Description("Move a card to a different lane and/or position (index) within that lane. If index is omitted, the card is appended to the end of the target lane.")]
    public async Task<string> MoveCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the target lane")] Guid laneId,
        [Description("The ID (guid) of the card to move (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("Optional 0-based index position in the target lane. Defaults to end of lane.")] int? index = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var card = await db.Cards.FindAsync([resolvedCardId!.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        if (!await db.Lanes.AnyAsync(l => l.Id == laneId, ct))
        {
            return "Error: Lane not found.";
        }

        var resolvedIndex = await MoveCardToLaneAsync(card, laneId, index, ct);

        card.LastUpdatedByUserId = user!.Id;
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return $"Card '{card.Name}' moved to lane at index {resolvedIndex}.";
    }

    [McpServerTool(Name = "update_card", Destructive = false)]
    [Description("Update a card's name, description, size, lane/position, or labels. All fields are optional — only provided fields are changed. For labelIds, pass a comma-separated list of label GUIDs to replace all current labels (use empty string to clear).")]
    public async Task<string> UpdateCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card to update (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("New name/title (optional)")] string? name = null,
        [Description("New markdown description (optional)")] string? descriptionMarkdown = null,
        [Description("New size ID (guid, optional)")] Guid? sizeId = null,
        [Description("New size name (e.g. 'M', 'XL', optional). Used if sizeId is not provided.")] string? sizeName = null,
        [Description("Target lane ID to move the card to (optional)")] Guid? laneId = null,
        [Description("0-based index position in the target lane (optional, requires laneId — defaults to end of lane)")] int? index = null,
        [Description("Comma-separated label GUIDs to replace current labels (optional, empty string clears all)")] string? labelIds = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        // No-op guard: if no optional params are provided, skip DB writes
        if (name is null && descriptionMarkdown is null && sizeId is null && sizeName is null && laneId is null && index is null && labelIds is null)
        {
            return "No changes specified.";
        }

        var card = await db.Cards.FindAsync([resolvedCardId!.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        if (name is not null)
        {
            card.Name = name;
        }

        if (descriptionMarkdown is not null)
        {
            card.DescriptionMarkdown = descriptionMarkdown;
        }

        if (sizeId.HasValue || sizeName is not null)
        {
            var cardBoardId = await db.Lanes.Where(l => l.Id == card.LaneId).Select(l => l.BoardId).FirstOrDefaultAsync(ct);
            var (resolvedSizeId, sizeError) = await ResolveSizeAsync(cardBoardId, sizeId, sizeName, ct);
            if (sizeError is not null)
            {
                return sizeError;
            }

            card.SizeId = resolvedSizeId!.Value;
        }

        // Lane move: if laneId provided, move card to that lane with optional index
        if (laneId is not null)
        {
            if (!await db.Lanes.AnyAsync(l => l.Id == laneId.Value, ct))
            {
                return "Error: Lane not found.";
            }

            await MoveCardToLaneAsync(card, laneId.Value, index, ct);
        }

        // Label replace: diff against current assignments
        if (labelIds is not null)
        {
            var cardBoardId = await db.Lanes.Where(l => l.Id == card.LaneId).Select(l => l.BoardId).FirstOrDefaultAsync(ct);
            var (desiredLabelIdList, labelError) = await ParseAndValidateLabelIdsAsync(labelIds, cardBoardId, ct);
            if (labelError is not null)
            {
                return labelError;
            }

            var desiredLabelIds = desiredLabelIdList.ToHashSet();
            var currentLabels = await db.CardLabels.Where(cl => cl.CardId == card.Id).ToListAsync(ct);
            var currentLabelIds = currentLabels.Select(cl => cl.LabelId).ToHashSet();

            // Remove labels no longer desired
            var toRemove = currentLabels.Where(cl => !desiredLabelIds.Contains(cl.LabelId)).ToList();
            db.CardLabels.RemoveRange(toRemove);

            // Add missing labels
            foreach (var lid in desiredLabelIds.Where(id => !currentLabelIds.Contains(id)))
            {
                db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = lid });
            }
        }

        card.LastUpdatedByUserId = user!.Id;
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_cards", ReadOnly = true, Destructive = false)]
    [Description("List cards for a board with optional filters. Use the 'since' filter to check for recent activity (includes cards with new/edited comments and new attachments).")]
    public async Task<string> GetCardsAsync(
        [Description("Your auth key")] string authKey,
        [Description("The board ID to list cards from")] Guid boardId,
        [Description("Only return cards with activity (created, updated, commented, attachment added) after this date. ISO 8601 format.")] DateTimeOffset? since = null,
        [Description("Only return cards with this label assigned")] Guid? labelId = null,
        [Description("Only return cards in this lane")] Guid? laneId = null,
        [Description("Search term. Prefix with # for card number lookup (e.g. '#42'). Plain numbers match card number or name/description. Text matches name or description.")] string? search = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Boards.AnyAsync(b => b.Id == boardId, ct))
        {
            return "Error: Board not found.";
        }

        var query = db.Cards.Where(c => c.BoardId == boardId);

        if (laneId.HasValue)
        {
            query = query.Where(c => c.LaneId == laneId.Value);
        }

        if (since.HasValue)
        {
            var cardIdsWithRecentComments = db.Comments
                .Where(cm => cm.LastUpdatedAtUtc >= since.Value)
                .Select(cm => cm.CardId);
            var cardIdsWithRecentAttachments = db.Attachments
                .Where(a => a.AddedAtUtc >= since.Value)
                .Select(a => a.CardId);

            query = query.Where(c =>
                c.CreatedAtUtc >= since.Value
                || c.LastUpdatedAtUtc >= since.Value
                || cardIdsWithRecentComments.Contains(c.Id)
                || cardIdsWithRecentAttachments.Contains(c.Id));
        }

        if (labelId.HasValue)
        {
            var cardIdsWithLabel = db.CardLabels.Where(cl => cl.LabelId == labelId.Value).Select(cl => cl.CardId);
            query = query.Where(c => cardIdsWithLabel.Contains(c.Id));
        }

        query = SearchHelper.ApplySearchFilter(query, search);

        var cards = await query.OrderBy(c => c.LaneId).ThenBy(c => c.Position).ToListAsync(ct);

        if (cards.Count == 0)
        {
            return JsonSerializer.Serialize(Array.Empty<CardSummary>(), JsonSerializerOptions.Web);
        }

        var cardIds = cards.Select(c => c.Id).ToList();

        // Batch load sizes
        var sizeIds = cards.Select(c => c.SizeId).Distinct().ToList();
        var sizeNames = await db.CardSizes
            .Where(s => sizeIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // Batch load labels
        var cardLabels = await db.CardLabels
            .Where(cl => cardIds.Contains(cl.CardId))
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => new { cl.CardId, Label = new CardLabelSummary(l.Id, l.Name, l.Color) })
            .ToListAsync(ct);
        var labelsByCard = cardLabels.GroupBy(x => x.CardId).ToDictionary(g => g.Key, g => g.Select(x => x.Label).ToList());

        // Batch load counts
        var commentCounts = await db.Comments
            .Where(cm => cardIds.Contains(cm.CardId))
            .GroupBy(cm => cm.CardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, ct);

        var attachmentCounts = await db.Attachments
            .Where(a => cardIds.Contains(a.CardId))
            .GroupBy(a => a.CardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CardId, x => x.Count, ct);

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

        return JsonSerializer.Serialize(summaries, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_card", ReadOnly = true, Destructive = false)]
    [Description("Get a single card by its ID or card number, including its comments, labels, and attachments (metadata only). To download attachment content, GET /api/v1/attachments/{id} with X-User-Key header.")]
    public async Task<string> GetCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var card = await db.Cards.FindAsync([resolvedCardId!.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        var comments = (await db.Comments
            .Where(c => c.CardId == card.Id)
            .ToListAsync(ct))
            .OrderBy(c => c.LastUpdatedAtUtc)
            .ToList();

        var userIds = comments.Select(c => c.UserId)
            .Append(card.CreatedByUserId)
            .Append(card.LastUpdatedByUserId)
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var commentsWithUserNames = comments.Select(c => new
        {
            c.Id,
            c.CardId,
            c.UserId,
            userName = userNames.GetValueOrDefault(c.UserId),
            c.ContentMarkdown,
            c.LastUpdatedAtUtc,
        });

        var labels = await db.CardLabels.Where(cl => cl.CardId == card.Id)
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => l)
            .ToListAsync(ct);

        var attachments = await db.Attachments
            .Where(a => a.CardId == card.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.AddedByUserId, a.AddedAtUtc })
            .ToListAsync(ct);

        var sizeName = await db.CardSizes.Where(s => s.Id == card.SizeId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "?";

        return JsonSerializer.Serialize(new
        {
            card,
            sizeName,
            createdByUserName = userNames.GetValueOrDefault(card.CreatedByUserId),
            lastUpdatedByUserName = userNames.GetValueOrDefault(card.LastUpdatedByUserId),
            comments = commentsWithUserNames,
            labels,
            attachments,
        }, JsonSerializerOptions.Web);
    }

    private async Task<int> MoveCardToLaneAsync(CardItem card, Guid targetLaneId, int? index, CancellationToken ct)
    {
        var sourceLaneId = card.LaneId;

        var targetCards = await db.Cards
            .Where(c => c.LaneId == targetLaneId && c.Id != card.Id)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

        var resolvedIndex = Math.Clamp(index ?? targetCards.Count, 0, targetCards.Count);
        targetCards.Insert(resolvedIndex, card);

        for (var i = 0; i < targetCards.Count; i++)
        {
            targetCards[i].Position = i * 10;
        }

        if (sourceLaneId != targetLaneId)
        {
            card.LaneId = targetLaneId;

            var sourceCards = await db.Cards
                .Where(c => c.LaneId == sourceLaneId && c.Id != card.Id)
                .OrderBy(c => c.Position)
                .ToListAsync(ct);

            for (var i = 0; i < sourceCards.Count; i++)
            {
                sourceCards[i].Position = i * 10;
            }
        }

        return resolvedIndex;
    }

    private async Task<(Guid? SizeId, string? Error)> ResolveSizeAsync(Guid boardId, Guid? sizeId, string? sizeName, CancellationToken ct)
    {
        if (sizeId.HasValue)
        {
            if (!await db.CardSizes.AnyAsync(s => s.Id == sizeId.Value && s.BoardId == boardId, ct))
            {
                return (null, "Error: Size not found or does not belong to this board.");
            }

            return (sizeId.Value, null);
        }

        if (!string.IsNullOrWhiteSpace(sizeName))
        {
            var size = await db.CardSizes.FirstOrDefaultAsync(s => s.BoardId == boardId && s.Name == sizeName, ct);
            if (size is null)
            {
                return (null, $"Error: Size '{sizeName}' not found on this board.");
            }

            return (size.Id, null);
        }

        var defaultSize = await db.CardSizes.Where(s => s.BoardId == boardId).OrderBy(s => s.Ordinal).FirstOrDefaultAsync(ct);
        if (defaultSize is null)
        {
            return (null, "Error: Board has no sizes configured.");
        }

        return (defaultSize.Id, null);
    }

    private async Task<(List<Guid> LabelIds, string? Error)> ParseAndValidateLabelIdsAsync(string? labelIds, Guid boardId, CancellationToken ct)
    {
        var parsedIds = new List<Guid>();
        if (string.IsNullOrWhiteSpace(labelIds))
        {
            return (parsedIds, null);
        }

        foreach (var part in labelIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var parsedId))
            {
                return (parsedIds, $"Error: Invalid label ID format: '{part}'. Expected a GUID.");
            }

            parsedIds.Add(parsedId);
        }

        var validLabels = await db.Labels
            .Where(l => parsedIds.Contains(l.Id) && l.BoardId == boardId)
            .Select(l => l.Id)
            .ToListAsync(ct);

        var invalidIds = parsedIds.Except(validLabels).ToList();
        if (invalidIds.Count > 0)
        {
            return (parsedIds, $"Error: Labels not found or not on the same board: {string.Join(", ", invalidIds)}");
        }

        return (parsedIds, null);
    }
}

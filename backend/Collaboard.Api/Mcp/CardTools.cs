using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class CardTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    private static readonly string[] _validSizes = ["S", "M", "L", "XL"];

    [McpServerTool(Name = "create_card", Destructive = false)]
    [Description("Create a new card on the kanban board.")]
    public async Task<string> CreateCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The title/name of the card")] string name,
        [Description("The ID (guid) of the lane to place the card in")] Guid laneId,
        [Description("Optional markdown description")] string? descriptionMarkdown = null,
        [Description("Size: S, M, L, or XL. Defaults to M")] string? size = null,
        [Description("Optional comma-separated label IDs (guids) to assign to the card at creation. All labels must belong to the same board as the lane.")] string? labelIds = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var cardSize = size ?? "M";
        if (!_validSizes.Contains(cardSize))
        {
            return "Error: Size must be one of: S, M, L, XL";
        }

        var lane = await db.Lanes.FirstOrDefaultAsync(l => l.Id == laneId, ct);
        if (lane is null)
        {
            return "Error: Lane not found.";
        }

        // Parse and validate label IDs if provided
        var parsedLabelIds = new List<Guid>();
        if (!string.IsNullOrWhiteSpace(labelIds))
        {
            var parts = labelIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!Guid.TryParse(part, out var parsedId))
                {
                    return $"Error: Invalid label ID format: '{part}'. Expected a GUID.";
                }

                parsedLabelIds.Add(parsedId);
            }

            // Validate all labels exist and belong to the same board as the lane
            foreach (var labelId in parsedLabelIds)
            {
                var label = await db.Labels.FindAsync([labelId], ct);
                if (label is null)
                {
                    return $"Error: Label '{labelId}' not found.";
                }

                if (label.BoardId != lane.BoardId)
                {
                    return $"Error: Label '{labelId}' does not belong to the same board as the target lane.";
                }
            }
        }

        var nextNumber = (await db.Cards.MaxAsync(c => (long?)c.Number, ct) ?? 0) + 1;
        var maxPosition = await db.Cards.Where(c => c.LaneId == laneId).MaxAsync(c => (int?)c.Position, ct) ?? -10;

        var now = DateTimeOffset.UtcNow;
        var card = new CardItem
        {
            Id = Guid.NewGuid(),
            Number = nextNumber,
            Name = name,
            DescriptionMarkdown = descriptionMarkdown ?? string.Empty,
            Size = cardSize,
            LaneId = laneId,
            Position = maxPosition + 10,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now,
            CreatedByUserId = user!.Id,
            LastUpdatedByUserId = user.Id,
        };
        db.Cards.Add(card);

        foreach (var labelId in parsedLabelIds)
        {
            db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = labelId });
        }

        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "move_card", Destructive = false)]
    [Description("Move a card to a different lane and/or position (index) within that lane. If index is omitted, the card is appended to the end of the target lane.")]
    public async Task<string> MoveCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card to move")] Guid cardId,
        [Description("The ID (guid) of the target lane")] Guid laneId,
        [Description("Optional 0-based index position in the target lane. Defaults to end of lane.")] int? index = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var card = await db.Cards.FindAsync([cardId], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        if (!await db.Lanes.AnyAsync(l => l.Id == laneId, ct))
        {
            return "Error: Lane not found.";
        }

        var sourceLaneId = card.LaneId;

        var targetCards = await db.Cards
            .Where(c => c.LaneId == laneId && c.Id != cardId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

        var resolvedIndex = Math.Clamp(index ?? targetCards.Count, 0, targetCards.Count);
        targetCards.Insert(resolvedIndex, card);

        for (var i = 0; i < targetCards.Count; i++)
        {
            targetCards[i].Position = i * 10;
        }

        if (sourceLaneId != laneId)
        {
            card.LaneId = laneId;

            var sourceCards = await db.Cards
                .Where(c => c.LaneId == sourceLaneId && c.Id != cardId)
                .OrderBy(c => c.Position)
                .ToListAsync(ct);

            for (var i = 0; i < sourceCards.Count; i++)
            {
                sourceCards[i].Position = i * 10;
            }
        }

        card.LastUpdatedByUserId = user!.Id;
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return $"Card '{card.Name}' moved to lane at index {resolvedIndex}.";
    }

    [McpServerTool(Name = "update_card", Destructive = false)]
    [Description("Update a card's name, description, or size.")]
    public async Task<string> UpdateCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card to update")] Guid cardId,
        [Description("New name/title (optional)")] string? name = null,
        [Description("New markdown description (optional)")] string? descriptionMarkdown = null,
        [Description("New size: S, M, L, or XL (optional)")] string? size = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var card = await db.Cards.FindAsync([cardId], ct);
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

        if (size is not null)
        {
            if (!_validSizes.Contains(size))
            {
                return "Error: Size must be one of: S, M, L, XL";
            }

            card.Size = size;
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
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var boardLaneIds = await db.Lanes.Where(l => l.BoardId == boardId).Select(l => l.Id).ToListAsync(ct);
        if (boardLaneIds.Count == 0)
        {
            return "Error: Board not found or has no lanes.";
        }

        var query = db.Cards.Where(c => boardLaneIds.Contains(c.LaneId));

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

        var cards = await query.OrderBy(c => c.LaneId).ThenBy(c => c.Position)
            .Select(c => new CardSummary(
                c.Id,
                c.Number,
                c.Name,
                c.DescriptionMarkdown,
                c.Size,
                c.LaneId,
                c.Position,
                c.CreatedByUserId,
                c.CreatedAtUtc,
                c.LastUpdatedByUserId,
                c.LastUpdatedAtUtc,
                db.CardLabels.Where(cl => cl.CardId == c.Id)
                    .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => new CardLabelSummary(l.Id, l.Name, l.Color))
                    .ToList(),
                db.Comments.Count(cm => cm.CardId == c.Id),
                db.Attachments.Count(a => a.CardId == c.Id)))
            .ToListAsync(ct);
        return JsonSerializer.Serialize(cards, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "get_card", ReadOnly = true, Destructive = false)]
    [Description("Get a single card by its ID, including its comments and labels.")]
    public async Task<string> GetCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card")] Guid cardId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var card = await db.Cards.FindAsync([cardId], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        var comments = await db.Comments.Where(c => c.CardId == cardId).ToListAsync(ct);
        comments.Sort((a, b) => a.LastUpdatedAtUtc.CompareTo(b.LastUpdatedAtUtc));
        var labels = await db.CardLabels.Where(cl => cl.CardId == cardId)
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (_, l) => l)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new { card, comments, labels }, JsonSerializerOptions.Web);
    }
}

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

        if (!await db.Lanes.AnyAsync(l => l.Id == laneId, ct))
        {
            return "Error: Lane not found.";
        }

        var nextNumber = (await db.Cards.MaxAsync(c => (long?)c.Number, ct) ?? 0) + 1;
        var maxPosition = await db.Cards.Where(c => c.LaneId == laneId).MaxAsync(c => (int?)c.Position, ct) ?? -10;

        var now = DateTimeOffset.UtcNow;
        var card = new CardItem
        {
            Id = Guid.NewGuid(),
            Number = nextNumber,
            Name = name,
            DescriptionMarkdown = descriptionMarkdown?.Replace("\\n", "\n") ?? string.Empty,
            Size = cardSize,
            LaneId = laneId,
            Position = maxPosition + 10,
            CreatedAtUtc = now,
            LastUpdatedAtUtc = now,
            CreatedByUserId = user!.Id,
            LastUpdatedByUserId = user.Id,
        };
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return JsonSerializer.Serialize(card, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "move_card", Destructive = false)]
    [Description("Move a card to a different lane and/or position (index) within that lane.")]
    public async Task<string> MoveCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card to move")] Guid cardId,
        [Description("The ID (guid) of the target lane")] Guid laneId,
        [Description("The 0-based index position in the target lane")] int index,
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

        index = Math.Clamp(index, 0, targetCards.Count);
        targetCards.Insert(index, card);

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
        return $"Card '{card.Name}' moved to lane at index {index}.";
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
            card.DescriptionMarkdown = descriptionMarkdown.Replace("\\n", "\n");
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

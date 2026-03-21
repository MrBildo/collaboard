using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class LabelTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "get_labels", ReadOnly = true, Destructive = false)]
    [Description("Get all labels for a specific board.")]
    public async Task<string> GetLabelsAsync(
        [Description("Your auth key")] string authKey,
        [Description("The board ID to list labels from")] Guid boardId,
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

        var labels = await db.Labels.Where(l => l.BoardId == boardId).OrderBy(l => l.Name).ToListAsync(ct);
        return JsonSerializer.Serialize(labels, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "add_label_to_card", Destructive = false)]
    [Description("Add a label to a card. Identify the card by cardId or cardNumber. Identify the label by labelId or labelName. The label must belong to the same board as the card.")]
    public async Task<string> AddLabelToCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("The ID (guid) of the label to add. Provide this or labelName, not both.")] Guid? labelId = null,
        [Description("The name of the label to add (matched case-insensitively within the card's board). Provide this or labelId, not both.")] string? labelName = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, cardResolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (cardResolveError is not null)
        {
            return cardResolveError;
        }

        if (await ArchiveGuard.IsCardArchivedAsync(db, resolvedCardId!.Value))
        {
            return "Archived cards cannot be modified.";
        }

        var card = await db.Cards.FindAsync([resolvedCardId.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        var cardBoardId = await db.Lanes.Where(l => l.Id == card.LaneId).Select(l => l.BoardId).FirstOrDefaultAsync(ct);

        var (resolvedLabelId, resolveError) = await ResolveLabelAsync(labelId, labelName, cardBoardId, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var label = await db.Labels.FindAsync([resolvedLabelId], ct);
        if (label is null)
        {
            return "Error: Label not found.";
        }

        if (label.BoardId != cardBoardId)
        {
            return "Error: Label does not belong to the same board as the card.";
        }

        if (await db.CardLabels.AnyAsync(cl => cl.CardId == card.Id && cl.LabelId == resolvedLabelId, ct))
        {
            return "Label already assigned to this card.";
        }

        db.CardLabels.Add(new CardLabel { CardId = card.Id, LabelId = resolvedLabelId!.Value });
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(cardBoardId);
        return "Label added successfully.";
    }

    [McpServerTool(Name = "remove_label_from_card", Destructive = false)]
    [Description("Remove a label from a card. Identify the card by cardId or cardNumber. Identify the label by labelId or labelName.")]
    public async Task<string> RemoveLabelFromCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card (provide this or cardNumber)")] Guid? cardId = null,
        [Description("The card number (provide this or cardId). Requires boardId or boardSlug.")] long? cardNumber = null,
        [Description("The ID (guid) of the label to remove. Provide this or labelName, not both.")] Guid? labelId = null,
        [Description("The name of the label to remove (matched case-insensitively within the card's board). Provide this or labelId, not both.")] string? labelName = null,
        [Description("Board ID (required when using cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId when using cardNumber)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (resolvedCardId, cardResolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (cardResolveError is not null)
        {
            return cardResolveError;
        }

        if (await ArchiveGuard.IsCardArchivedAsync(db, resolvedCardId!.Value))
        {
            return "Archived cards cannot be modified.";
        }

        var card = await db.Cards.FindAsync([resolvedCardId.Value], ct);
        if (card is null)
        {
            return "Error: Card not found.";
        }

        var cardBoardId = await db.Lanes.Where(l => l.Id == card.LaneId).Select(l => l.BoardId).FirstOrDefaultAsync(ct);

        var (resolvedLabelId, resolveError) = await ResolveLabelAsync(labelId, labelName, cardBoardId, ct);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var cardLabel = await db.CardLabels.FirstOrDefaultAsync(cl => cl.CardId == card.Id && cl.LabelId == resolvedLabelId, ct);
        if (cardLabel is null)
        {
            return "Error: Label not assigned to this card.";
        }

        db.CardLabels.Remove(cardLabel);
        await db.SaveChangesAsync(ct);
        await db.PublishForCardAsync(card.Id, broadcaster);
        return "Label removed successfully.";
    }

    private async Task<(Guid? LabelId, string? Error)> ResolveLabelAsync(
        Guid? labelId, string? labelName, Guid boardId, CancellationToken ct)
    {
        var hasId = labelId.HasValue && labelId.Value != Guid.Empty;
        var hasName = !string.IsNullOrWhiteSpace(labelName);

        if (hasId && hasName)
        {
            return (null, "Error: Provide either labelId or labelName, not both.");
        }

        if (!hasId && !hasName)
        {
            return (null, "Error: Provide either labelId or labelName.");
        }

        if (hasId)
        {
            return (labelId, null);
        }

        var matches = await db.Labels
            .Where(l => l.BoardId == boardId && EF.Functions.Collate(l.Name, "NOCASE") == labelName!)
            .ToListAsync(ct);

        if (matches.Count == 0)
        {
            var available = await db.Labels
                .Where(l => l.BoardId == boardId)
                .OrderBy(l => l.Name)
                .Select(l => l.Name)
                .ToListAsync(ct);

            var availableList = available.Count > 0
                ? string.Join(", ", available)
                : "(none)";

            return (null, $"Error: No label named '{labelName}' found on this board. Available labels: {availableList}");
        }

        if (matches.Count > 1)
        {
            return (null, $"Error: Multiple labels named '{labelName}' found on this board. Use labelId instead.");
        }

        return (matches[0].Id, null);
    }
}

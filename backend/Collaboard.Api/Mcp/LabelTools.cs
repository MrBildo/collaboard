using System.ComponentModel;
using System.Text.Json;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class LabelTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "get_labels", ReadOnly = true, Destructive = false)]
    [Description("Get all available labels.")]
    public async Task<string> GetLabelsAsync(
        [Description("Your auth key")] string authKey,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var labels = await db.Labels.OrderBy(l => l.Name).ToListAsync(ct);
        return JsonSerializer.Serialize(labels, JsonSerializerOptions.Web);
    }

    [McpServerTool(Name = "add_label_to_card", Destructive = false)]
    [Description("Add a label to a card.")]
    public async Task<string> AddLabelToCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card")] Guid cardId,
        [Description("The ID (guid) of the label to add")] Guid labelId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        if (!await db.Cards.AnyAsync(c => c.Id == cardId, ct))
        {
            return "Error: Card not found.";
        }

        if (!await db.Labels.AnyAsync(l => l.Id == labelId, ct))
        {
            return "Error: Label not found.";
        }

        if (await db.CardLabels.AnyAsync(cl => cl.CardId == cardId && cl.LabelId == labelId, ct))
        {
            return "Label already assigned to this card.";
        }

        db.CardLabels.Add(new CardLabel { CardId = cardId, LabelId = labelId });
        await db.SaveChangesAsync(ct);
        broadcaster.Publish("board-updated");
        return "Label added successfully.";
    }

    [McpServerTool(Name = "remove_label_from_card", Destructive = false)]
    [Description("Remove a label from a card.")]
    public async Task<string> RemoveLabelFromCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("The ID (guid) of the card")] Guid cardId,
        [Description("The ID (guid) of the label to remove")] Guid labelId,
        CancellationToken ct = default)
    {
        var (_, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var cardLabel = await db.CardLabels.FirstOrDefaultAsync(cl => cl.CardId == cardId && cl.LabelId == labelId, ct);
        if (cardLabel is null)
        {
            return "Error: Label not assigned to this card.";
        }

        db.CardLabels.Remove(cardLabel);
        await db.SaveChangesAsync(ct);
        broadcaster.Publish("board-updated");
        return "Label removed successfully.";
    }
}

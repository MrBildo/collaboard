using System.ComponentModel;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

[McpServerToolType]
public sealed class ArchiveTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "archive_card", Destructive = false)]
    [Description("Archive a card — hides it from normal views but preserves it for reference. All roles can archive.")]
    public async Task<string> ArchiveCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("Card ID (provide this or cardNumber)")] Guid? cardId = null,
        [Description("Card number (requires boardId or boardSlug)")] long? cardNumber = null,
        [Description("Board ID (required with cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, authError) = await auth.RequireUserAsync(authKey, ct);
        if (user is null)
        {
            return authError!;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolvedCardId is null)
        {
            return resolveError!;
        }

        var card = await db.Cards.FindAsync([resolvedCardId.Value], ct);
        if (card is null)
        {
            return "Card not found.";
        }

        var currentLane = await db.Lanes.FindAsync([card.LaneId], ct);
        if (currentLane is not null && currentLane.IsArchiveLane)
        {
            return "Card is already archived.";
        }

        var archiveLane = await db.Lanes.FirstOrDefaultAsync(l => l.BoardId == card.BoardId && l.IsArchiveLane, ct);
        if (archiveLane is null)
        {
            return "Board has no archive lane.";
        }

        await CardReorderHelper.MoveCardToLaneAsync(db, card, archiveLane.Id, 0, ct);
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        card.LastUpdatedByUserId = user.Id;
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(card.BoardId);

        return $"Card #{card.Number} archived.";
    }

    [McpServerTool(Name = "restore_card", Destructive = false)]
    [Description("Restore an archived card to a specified lane.")]
    public async Task<string> RestoreCardAsync(
        [Description("Your auth key")] string authKey,
        [Description("Target lane ID (required)")] Guid laneId,
        [Description("Card ID (provide this or cardNumber)")] Guid? cardId = null,
        [Description("Card number (requires boardId or boardSlug)")] long? cardNumber = null,
        [Description("Board ID (required with cardNumber)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, authError) = await auth.RequireUserAsync(authKey, ct);
        if (user is null)
        {
            return authError!;
        }

        var (resolvedCardId, resolveError) = await McpCardResolver.ResolveCardIdAsync(db, cardId, cardNumber, boardId, boardSlug, ct);
        if (resolvedCardId is null)
        {
            return resolveError!;
        }

        var card = await db.Cards.FindAsync([resolvedCardId.Value], ct);
        if (card is null)
        {
            return "Card not found.";
        }

        var currentLane = await db.Lanes.FindAsync([card.LaneId], ct);
        if (currentLane is null || !currentLane.IsArchiveLane)
        {
            return "Card is not archived.";
        }

        var targetLane = await db.Lanes.FindAsync([laneId], ct);
        if (targetLane is null)
        {
            return "Lane not found.";
        }

        if (targetLane.IsArchiveLane)
        {
            return "Cannot restore to an archive lane.";
        }

        if (targetLane.BoardId != card.BoardId)
        {
            return "Lane does not belong to this board.";
        }

        await CardReorderHelper.MoveCardToLaneAsync(db, card, targetLane.Id, 0, ct);
        card.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        card.LastUpdatedByUserId = user.Id;
        await db.SaveChangesAsync(ct);
        broadcaster.PublishBoardUpdated(card.BoardId);

        return $"Card #{card.Number} restored to lane '{targetLane.Name}'.";
    }
}

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Collaboard.Api.Endpoints;
using Collaboard.Api.Events;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Collaboard.Api.Mcp;

// Card #196 / #243 Phase 5: bulk card tools. Three all-roles tools that batch the
// per-card archive_card / restore_card / update_card analogs over N cards in one
// call. Each follows the two-phase contract from agent-admin-mcp.md Part 3:
//
//   Phase 1 — pre-validation (fail loud, single "Error: ..." string, NO mutations):
//     ref shape/parse, card existence (one round-trip), and operation premises
//     (restore: all cards' boards match the target lane's board; update: target
//     lane/size/labels exist, are not archive-lane, and are board-consistent with
//     every card). Any failure here returns one error string and writes nothing.
//
//   Phase 2 — per-card execution (best-effort, per-item envelope):
//     iterate cards in stable input order; capture per-card ok/error; one card's
//     failure never aborts the loop. All staged changes persist in a SINGLE
//     SaveChangesAsync at the end, and each affected board is broadcast exactly
//     ONCE (deduplicated). If the final SaveChanges itself throws, the whole batch
//     is reported failed via an error string.
//
// These are all-roles tools — they gate via RequireUserAsync (the per-card analogs
// they batch are all-roles today), NOT RequireAdminLevelAsync.
[McpServerToolType]
public sealed class BulkCardTools(BoardDbContext db, McpAuthService auth, BoardEventBroadcaster broadcaster)
{
    [McpServerTool(Name = "bulk_archive_cards", Destructive = false)]
    [Description("Archive multiple cards in a single call (move them to their boards' archive lanes). Provide cardIds (CSV of card GUIDs) OR cardNumbers (CSV) + boardId/boardSlug, not both. Pre-validates all refs (fails loud with no mutations if any is invalid or missing), then archives best-effort. Returns a per-card result envelope: { totalRequested, succeeded, failed, results: [{ cardId, number, status, error? }] } aligned 1:1 with the input order.")]
    public async Task<string> BulkArchiveCardsAsync(
        [Description("Your auth key")] string authKey,
        [Description("CSV of card GUIDs (provide this OR cardNumbers)")] string? cardIds = null,
        [Description("CSV of card numbers (requires boardId or boardSlug)")] string? cardNumbers = null,
        [Description("Board ID (required with cardNumbers)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId, with cardNumbers)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (cards, refError) = await McpCardResolver.ResolveCardRefsAsync(db, cardIds, cardNumbers, boardId, boardSlug, ct);
        if (refError is not null)
        {
            return refError;
        }

        // Archive lanes for the affected boards (one round-trip; keyed by board).
        var affectedBoardIds = cards!.Select(c => c.BoardId).Distinct().ToList();
        var archiveLanes = await db.Lanes
            .Where(l => affectedBoardIds.Contains(l.BoardId) && l.IsArchiveLane)
            .ToListAsync(ct);
        var archiveLaneByBoard = archiveLanes.ToDictionary(l => l.BoardId, l => l.Id);
        var archiveLaneIds = archiveLanes.Select(l => l.Id).ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var execution = new BulkExecution(db, broadcaster, user!.Id, now);

        await execution.RunAsync(cards!, async card =>
        {
            if (archiveLaneIds.Contains(card.LaneId))
            {
                return "Card is already archived.";
            }

            if (!archiveLaneByBoard.TryGetValue(card.BoardId, out var archiveLaneId))
            {
                return "Board has no archive lane.";
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, archiveLaneId, 0, ct);
            return null;
        });

        return await execution.SaveAndSerializeAsync(ct);
    }

    [McpServerTool(Name = "bulk_restore_cards", Destructive = false)]
    [Description("Restore multiple archived cards to a single target lane in one call. Provide cardIds (CSV of card GUIDs) OR cardNumbers (CSV) + boardId/boardSlug, not both. All cards must be on the same board as the target lane — cross-board mixing is rejected up-front with no mutations. Pre-validates all refs and the board match, then restores best-effort. Returns a per-card result envelope: { totalRequested, succeeded, failed, results: [{ cardId, number, status, error? }] }.")]
    public async Task<string> BulkRestoreCardsAsync(
        [Description("Your auth key")] string authKey,
        [Description("Target lane ID to restore the cards into (required)")] Guid targetLaneId,
        [Description("CSV of card GUIDs (provide this OR cardNumbers)")] string? cardIds = null,
        [Description("CSV of card numbers (requires boardId or boardSlug)")] string? cardNumbers = null,
        [Description("Board ID (required with cardNumbers)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId, with cardNumbers)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var (cards, refError) = await McpCardResolver.ResolveCardRefsAsync(db, cardIds, cardNumbers, boardId, boardSlug, ct);
        if (refError is not null)
        {
            return refError;
        }

        // Phase 1 premise: target lane exists, is not an archive lane, and every
        // card is on the same board as the target lane.
        var targetLane = await db.Lanes.FindAsync([targetLaneId], ct);
        if (targetLane is null)
        {
            return "Error: Lane not found.";
        }

        if (targetLane.IsArchiveLane)
        {
            return "Error: Cannot restore to an archive lane.";
        }

        var crossBoard = cards!.Where(c => c.BoardId != targetLane.BoardId).ToList();
        if (crossBoard.Count > 0)
        {
            return $"Error: All cards must be on the target lane's board. Cards not on board {targetLane.BoardId}: {string.Join(", ", crossBoard.Select(c => $"#{c.Number}"))}";
        }

        var archiveLaneIds = await db.Lanes
            .Where(l => l.BoardId == targetLane.BoardId && l.IsArchiveLane)
            .Select(l => l.Id)
            .ToListAsync(ct);
        var archiveLaneIdSet = archiveLaneIds.ToHashSet();

        var now = DateTimeOffset.UtcNow;
        var execution = new BulkExecution(db, broadcaster, user!.Id, now);

        await execution.RunAsync(cards!, async card =>
        {
            if (!archiveLaneIdSet.Contains(card.LaneId))
            {
                return "Card is not archived.";
            }

            await CardReorderHelper.MoveCardToLaneAsync(db, card, targetLaneId, 0, ct);
            return null;
        });

        return await execution.SaveAndSerializeAsync(ct);
    }

    [McpServerTool(Name = "bulk_update_cards", Destructive = false)]
    [Description("Apply a uniform update to multiple cards in one call — lane/position move, size change, and/or label-set replace. (bulk_move_cards is folded in here: pass laneId to move N cards to one lane.) Per-card name/description bulk update is NOT offered. Provide cardIds (CSV of card GUIDs) OR cardNumbers (CSV) + boardId/boardSlug, not both. For labelIds, pass a CSV of label GUIDs or a JSON array string to replace all current labels (empty clears all). When laneId, sizeId/sizeName, or labelIds is provided, all cards must be on the same board as that target (validated up-front, no mutations on failure). Archived cards are rejected per-card. Returns a per-card result envelope: { totalRequested, succeeded, failed, results: [{ cardId, number, status, error? }] }.")]
    public async Task<string> BulkUpdateCardsAsync(
        [Description("Your auth key")] string authKey,
        [Description("CSV of card GUIDs (provide this OR cardNumbers)")] string? cardIds = null,
        [Description("CSV of card numbers (requires boardId or boardSlug)")] string? cardNumbers = null,
        [Description("Target lane ID to move all cards to (optional)")] Guid? laneId = null,
        [Description("0-based index position in the target lane (optional, requires laneId — defaults to top of lane). Note: cards are placed sequentially, so the relative order of the batch is preserved.")] int? index = null,
        [Description("New size ID (guid, optional) for all cards")] Guid? sizeId = null,
        [Description("New size name (e.g. 'M', 'XL', optional) for all cards. Used if sizeId is not provided.")] string? sizeName = null,
        [Description("Label GUIDs to replace current labels on all cards (optional). Accepts comma-separated ('guid1,guid2') or a JSON array string ('[\"guid1\",\"guid2\"]'). Empty string or empty array clears all.")] string? labelIds = null,
        [Description("Board ID (required with cardNumbers)")] Guid? boardId = null,
        [Description("Board slug (alternative to boardId, with cardNumbers)")] string? boardSlug = null,
        CancellationToken ct = default)
    {
        var (user, error) = await auth.RequireUserAsync(authKey, ct);
        if (error is not null)
        {
            return error;
        }

        var hasLane = laneId.HasValue;
        var hasSize = sizeId.HasValue || sizeName is not null;
        var hasLabels = labelIds is not null;
        if (!hasLane && !hasSize && !hasLabels)
        {
            return "Error: No changes specified. Provide laneId, sizeId/sizeName, and/or labelIds.";
        }

        var (cards, refError) = await McpCardResolver.ResolveCardRefsAsync(db, cardIds, cardNumbers, boardId, boardSlug, ct);
        if (refError is not null)
        {
            return refError;
        }

        // Phase 1 premise: a uniform update can only apply across cards that share a
        // board (lane/size/label are all board-scoped). When any board-scoped field
        // is set, every card must be on a single common board.
        var distinctBoardIds = cards!.Select(c => c.BoardId).Distinct().ToList();
        if ((hasLane || hasSize || hasLabels) && distinctBoardIds.Count > 1)
        {
            return "Error: A uniform lane/size/label update requires all cards to be on the same board.";
        }

        var commonBoardId = distinctBoardIds[0];

        // Target lane: exists, belongs to the common board, not an archive lane.
        if (laneId.HasValue)
        {
            var targetLane = await db.Lanes.FindAsync([laneId.Value], ct);
            if (targetLane is null)
            {
                return "Error: Lane not found.";
            }

            if (targetLane.IsArchiveLane)
            {
                return "Error: Cannot move cards to an archive lane. Use bulk_archive_cards.";
            }

            if (targetLane.BoardId != commonBoardId)
            {
                return "Error: Target lane does not belong to the cards' board.";
            }
        }

        // Target size: resolve once against the common board.
        Guid? resolvedSizeId = null;
        if (hasSize)
        {
            var (sid, sizeError) = await ResolveSizeAsync(commonBoardId, sizeId, sizeName, ct);
            if (sizeError is not null)
            {
                return sizeError;
            }

            resolvedSizeId = sid;
        }

        // Target labels: parse + validate once against the common board.
        List<Guid>? desiredLabelIds = null;
        if (hasLabels)
        {
            var (parsed, labelError) = await ParseAndValidateLabelIdsAsync(labelIds, commonBoardId, ct);
            if (labelError is not null)
            {
                return labelError;
            }

            desiredLabelIds = parsed;
        }

        var now = DateTimeOffset.UtcNow;
        var execution = new BulkExecution(db, broadcaster, user!.Id, now);

        await execution.RunAsync(cards!, async card =>
        {
            if (await ArchiveGuard.IsCardArchivedAsync(db, card.Id))
            {
                return "Archived cards cannot be edited. Restore the card first.";
            }

            if (resolvedSizeId.HasValue)
            {
                card.SizeId = resolvedSizeId.Value;
            }

            if (laneId.HasValue)
            {
                await CardReorderHelper.MoveCardToLaneAsync(db, card, laneId.Value, index, ct);
            }

            if (desiredLabelIds is not null)
            {
                await ApplyLabelSetAsync(card.Id, desiredLabelIds, ct);
            }

            return null;
        });

        return await execution.SaveAndSerializeAsync(ct);
    }

    private async Task ApplyLabelSetAsync(Guid cardId, List<Guid> desiredLabelIds, CancellationToken ct)
    {
        var desired = desiredLabelIds.ToHashSet();
        var current = await db.CardLabels.Where(cl => cl.CardId == cardId).ToListAsync(ct);
        var currentIds = current.Select(cl => cl.LabelId).ToHashSet();

        var toRemove = current.Where(cl => !desired.Contains(cl.LabelId)).ToList();
        db.CardLabels.RemoveRange(toRemove);

        foreach (var lid in desired.Where(id => !currentIds.Contains(id)))
        {
            db.CardLabels.Add(new CardLabel { CardId = cardId, LabelId = lid });
        }
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

        var size = await db.CardSizes.FirstOrDefaultAsync(s => s.BoardId == boardId && s.Name == sizeName, ct);
        if (size is null)
        {
            return (null, $"Error: Size '{sizeName}' not found on this board.");
        }

        return (size.Id, null);
    }

    // Mirrors CardTools.ParseAndValidateLabelIdsAsync — same CSV-or-JSON-array
    // permissiveness (#241) and same cross-board rejection contract. Kept local
    // rather than shared because the bulk pre-validation phase needs it to run
    // once against the common board, and the per-card analog runs it per call.
    private async Task<(List<Guid> LabelIds, string? Error)> ParseAndValidateLabelIdsAsync(string? labelIds, Guid boardId, CancellationToken ct)
    {
        List<Guid> parsedIds = [];
        if (string.IsNullOrWhiteSpace(labelIds))
        {
            return (parsedIds, null);
        }

        var parts = TryParseJsonStringArray(labelIds, out var jsonArrayParts)
            ? jsonArrayParts
            : labelIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
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

    private static bool TryParseJsonStringArray(string value, out string[] parts)
    {
        parts = [];
        var trimmed = value.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string[]>(value);
            if (deserialized is null)
            {
                return false;
            }

            parts = [.. deserialized
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .Select(static s => s.Trim())];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// The per-card execution engine shared by all three bulk tools. Owns the Phase-2
// contract: iterate in stable order, capture per-card ok/error without aborting,
// stamp the actor/timestamp on cards that mutated, then a SINGLE SaveChangesAsync
// and ONE deduplicated broadcast per affected board. A SaveChanges throw collapses
// the whole batch to a single error string (the only place "all-or-nothing"
// genuinely applies — at the persistence layer, per the spec).
file sealed class BulkExecution(BoardDbContext db, BoardEventBroadcaster broadcaster, Guid userId, DateTimeOffset now)
{
    private readonly List<BulkCardResult> _results = [];
    private readonly HashSet<Guid> _affectedBoardIds = [];

    public async Task RunAsync(List<CardItem> cards, Func<CardItem, Task<string?>> operation)
    {
        foreach (var card in cards)
        {
            try
            {
                var perCardError = await operation(card);
                if (perCardError is not null)
                {
                    _results.Add(new BulkCardResult(card.Id, card.Number, "error", perCardError));
                    continue;
                }

                card.LastUpdatedAtUtc = now;
                card.LastUpdatedByUserId = userId;
                _affectedBoardIds.Add(card.BoardId);
                _results.Add(new BulkCardResult(card.Id, card.Number, "ok", null));
            }
#pragma warning disable CA1031 // Per-card best-effort: one card's failure must not abort the batch; the error is captured in the envelope.
            catch (Exception ex)
            {
                _results.Add(new BulkCardResult(card.Id, card.Number, "error", ex.Message));
            }
#pragma warning restore CA1031
        }
    }

    public async Task<string> SaveAndSerializeAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
#pragma warning disable CA1031 // A SaveChanges failure collapses the whole batch — report it as a single error string per the spec.
        catch (Exception ex)
        {
            return $"Error: Failed to persist bulk operation; no changes were saved. {ex.Message}";
        }
#pragma warning restore CA1031

        foreach (var boardId in _affectedBoardIds)
        {
            broadcaster.PublishBoardUpdated(boardId);
        }

        var succeeded = _results.Count(r => r.Status == "ok");
        var envelope = new BulkResultEnvelope(_results.Count, succeeded, _results.Count - succeeded, _results);
        return JsonSerializer.Serialize(envelope, JsonSerializerOptions.Web);
    }
}

// Error is omitted on "ok" results so the envelope matches the spec example shape
// ({ cardId, number, status } for ok; + error for non-ok).
file record BulkCardResult(
    Guid CardId,
    long Number,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);

file record BulkResultEnvelope(int TotalRequested, int Succeeded, int Failed, List<BulkCardResult> Results);

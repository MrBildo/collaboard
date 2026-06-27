using Collaboard.Api.Endpoints;
using Collaboard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Collaboard.Api.Events;

// The anti-drift core for webhook event emission (#320). Per the project convention —
// every card mutation has two callers (REST + MCP) sharing a helper core; drift between
// them is the top bug class — the eight converted sites must NOT each hand-build a
// BoardEvent. All three create sites call PublishCardCreatedAsync; all five move sites
// call PublishCardMovedAsync. So REST and MCP emit identical events by construction
// (the same remedy as CardSummaryBuilder / SearchHelper).
//
// Everything the wire envelope needs is resolved here, at emit-time, before enqueuing
// (D1 — fidelity-required): the fat CardSummary projection, the board slug, the lane
// name(s), a fresh ULID eventId, the occurredAt timestamp. The dispatcher (Phase 2)
// then needs no DB.
internal static class WebhookEventFactory
{
    // The event-type identifiers now live in WebhookEventTypes (the catalog SoT — #326 promoted
    // them per v1's Promised Upgrade Path), referenced below so a site can't emit "card.moved"
    // while a test asserts "card_moved". EventVersion is a wire-envelope version, not an event
    // type, so it stays here.
    public const string EventVersion = "1";

    public static async Task PublishCardCreatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var summary = await BuildSummaryAsync(db, card, ct);
        var (boardSlug, laneName) = await ResolveBoardSlugAndLaneNameAsync(db, card.BoardId, card.LaneId, ct);

        var boardEvent = BuildEvent
        (
            WebhookEventTypes.CardCreated,
            card.BoardId,
            boardSlug,
            actor,
            new WebhookCardCreatedData(summary, laneName)
        );

        broadcaster.Publish(boardEvent);
    }

    // fromLane / fromPosition are captured by the call-site BEFORE the move mutates the
    // card (MoveCardToLaneAsync — or the PATCH site's inline mutation — renumbers both
    // lanes, so the source position is gone once it runs). By call time the card already
    // sits in the target lane at the target position. Used by the four single-card move
    // sites, which fan out to SSE + webhook together via broadcaster.Publish.
    public static async Task PublishCardMovedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        Lane fromLane,
        int fromPosition,
        Lane toLane,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardMovedAsync(db, card, fromLane, fromPosition, toLane, actor, ct);
        broadcaster.Publish(boardEvent);
    }

    // Builds the card.moved event WITHOUT publishing — for the bulk path
    // (bulk_update_cards), where the webhook projection needs one event PER moved card but
    // the SSE projection coalesces to one board-bell per board (BulkExecution's existing
    // contract). The bulk tool enqueues these directly to the sink after the batch saves,
    // and rings the single SSE bell separately — so calling broadcaster.Publish per card
    // (which would ring N SSE bells) would break the coalesce safety property. (#320.)
    public static async Task<BoardEvent> BuildCardMovedAsync
    (
        BoardDbContext db,
        CardItem card,
        Lane fromLane,
        int fromPosition,
        Lane toLane,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var summary = await BuildSummaryAsync(db, card, ct);
        var boardSlug = await ResolveBoardSlugAsync(db, card.BoardId, ct);

        var data = new WebhookCardMovedData
        (
            summary,
            toLane.Name,
            new WebhookLaneRef(fromLane.Id, fromLane.Name, fromPosition),
            new WebhookLaneRef(toLane.Id, toLane.Name, card.Position)
        );

        return BuildEvent(WebhookEventTypes.CardMoved, card.BoardId, boardSlug, actor, data);
    }

    // ── Card-family M2 emit helpers (#329) ──────────────────────────────────────────
    //
    // Build* methods produce a BoardEvent WITHOUT publishing — the co-fire sites
    // (PATCH /cards, update_card) collect several into one list and ring a single SSE
    // bell via broadcaster.PublishCoalesced; the bulk/prune sites enqueue them directly
    // to the sink. Publish* convenience wrappers cover the single-axis sites (archive,
    // restore, one label add/remove) where exactly one event fans out to one SSE bell.

    public static Task<BoardEvent> BuildCardUpdatedAsync
    (
        BoardDbContext db,
        CardItem card,
        BoardUser actor,
        CancellationToken ct
    ) =>
        BuildCardSummaryEventAsync(db, WebhookEventTypes.CardUpdated, card, actor, (summary, laneName) => new WebhookCardUpdatedData(summary, laneName), ct);

    public static Task<BoardEvent> BuildCardLabeledAsync
    (
        BoardDbContext db,
        CardItem card,
        Label label,
        BoardUser actor,
        CancellationToken ct
    ) =>
        BuildCardSummaryEventAsync(db, WebhookEventTypes.CardLabeled, card, actor, (summary, laneName) => new WebhookCardLabeledData(summary, laneName, new WebhookLabelRef(label.Id, label.Name, label.Color)), ct);

    public static Task<BoardEvent> BuildCardUnlabeledAsync
    (
        BoardDbContext db,
        CardItem card,
        Label label,
        BoardUser actor,
        CancellationToken ct
    ) =>
        BuildCardSummaryEventAsync(db, WebhookEventTypes.CardUnlabeled, card, actor, (summary, laneName) => new WebhookCardUnlabeledData(summary, laneName, new WebhookLabelRef(label.Id, label.Name, label.Color)), ct);

    public static async Task PublishCardArchivedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardSummaryEventAsync(db, WebhookEventTypes.CardArchived, card, actor, (summary, laneName) => new WebhookCardArchivedData(summary, laneName), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishCardRestoredAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardSummaryEventAsync(db, WebhookEventTypes.CardRestored, card, actor, (summary, laneName) => new WebhookCardRestoredData(summary, laneName), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishCardLabeledAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        Label label,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardLabeledAsync(db, card, label, actor, ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishCardUnlabeledAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardItem card,
        Label label,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCardUnlabeledAsync(db, card, label, actor, ct);
        broadcaster.Publish(boardEvent);
    }

    // Batch builders for the bulk (bulk_archive_cards / bulk_restore_cards) and prune-archive
    // paths: one CardSummaryBuilder pass + one slug query + one lane-name query for the whole
    // set, so an N-card archive does not fan out into N×(summary queries) on a concurrent
    // board. Cards may span boards/lanes (bulk_archive accepts a cross-board set), so slug and
    // lane name are resolved by id. The caller enqueues the events to the sink and rings one
    // bell per affected board (the BulkExecution coalesce contract).
    public static Task<List<BoardEvent>> BuildCardArchivedBatchAsync
    (
        BoardDbContext db,
        IReadOnlyList<CardItem> cards,
        BoardUser actor,
        CancellationToken ct
    ) =>
        BuildCardSummaryEventBatchAsync(db, WebhookEventTypes.CardArchived, cards, actor, (summary, laneName) => new WebhookCardArchivedData(summary, laneName), ct);

    public static Task<List<BoardEvent>> BuildCardRestoredBatchAsync
    (
        BoardDbContext db,
        IReadOnlyList<CardItem> cards,
        BoardUser actor,
        CancellationToken ct
    ) =>
        BuildCardSummaryEventBatchAsync(db, WebhookEventTypes.CardRestored, cards, actor, (summary, laneName) => new WebhookCardRestoredData(summary, laneName), ct);

    // The multi-axis co-fire assembly (#329) — the shared REST/MCP seam so PATCH /cards and
    // update_card emit the IDENTICAL event set for the same change by construction (the
    // anti-drift discipline; this must NOT be re-implemented per surface). One event per
    // CHANGED axis: content → card.updated, lane → card.moved, each added/removed label →
    // card.labeled / card.unlabeled. The caller rings exactly one SSE bell via
    // PublishCoalesced. Build* (no publish) so the events ride one coalesced bell, not N.
    public static async Task<List<BoardEvent>> BuildCardUpdateEventsAsync
    (
        BoardDbContext db,
        CardItem card,
        BoardUser actor,
        bool contentChanged,
        Lane? moveToLane,
        Lane? moveFromLane,
        int moveFromPosition,
        IReadOnlyList<Guid> addedLabelIds,
        IReadOnlyList<Guid> removedLabelIds,
        CancellationToken ct
    )
    {
        List<BoardEvent> events = [];

        if (contentChanged)
        {
            events.Add(await BuildCardUpdatedAsync(db, card, actor, ct));
        }

        if (moveToLane is not null)
        {
            events.Add(await BuildCardMovedAsync(db, card, moveFromLane!, moveFromPosition, moveToLane, actor, ct));
        }

        if (addedLabelIds.Count == 0 && removedLabelIds.Count == 0)
        {
            return events;
        }

        var changedLabelIds = addedLabelIds.Concat(removedLabelIds).ToList();
        var labelsById = await db.Labels
            .Where(l => changedLabelIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);

        foreach (var labelId in addedLabelIds)
        {
            if (labelsById.TryGetValue(labelId, out var label))
            {
                events.Add(await BuildCardLabeledAsync(db, card, label, actor, ct));
            }
        }

        foreach (var labelId in removedLabelIds)
        {
            if (labelsById.TryGetValue(labelId, out var label))
            {
                events.Add(await BuildCardUnlabeledAsync(db, card, label, actor, ct));
            }
        }

        return events;
    }

    // ── Comment-family M2 emit helpers (#329) ───────────────────────────────────────
    //
    // A comment mutation rings the same single board bell it always has (one "board-updated")
    // AND fans out one webhook event — routing REST and MCP through these helpers keeps the two
    // surfaces identical by construction. comment.deleted is published AFTER the row is removed,
    // from the captured comment object (its fields stay in memory); the card it belonged to still
    // exists, so the card ref still resolves.

    public static async Task PublishCommentCreatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardComment comment,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCommentEventAsync(db, WebhookEventTypes.CommentCreated, comment, actor, (c, card) => new WebhookCommentCreatedData(c, card), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishCommentUpdatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardComment comment,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCommentEventAsync(db, WebhookEventTypes.CommentUpdated, comment, actor, (c, card) => new WebhookCommentUpdatedData(c, card), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishCommentDeletedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardComment comment,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildCommentEventAsync(db, WebhookEventTypes.CommentDeleted, comment, actor, (c, card) => new WebhookCommentDeletedData(c, card), ct);
        broadcaster.Publish(boardEvent);
    }

    // ── Label-resource-family M2 emit helpers (#329) ─────────────────────────────────
    //
    // The board-scoped label lifecycle (distinct from card.labeled / card.unlabeled). Same single
    // board bell the label CRUD sites always rang plus one webhook event. label.deleted is
    // published AFTER the row is removed, from the captured label object.

    public static async Task PublishLabelCreatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        Label label,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildLabelEventAsync(db, WebhookEventTypes.LabelCreated, label, actor, l => new WebhookLabelCreatedData(l), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishLabelUpdatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        Label label,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildLabelEventAsync(db, WebhookEventTypes.LabelUpdated, label, actor, l => new WebhookLabelUpdatedData(l), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishLabelDeletedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        Label label,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildLabelEventAsync(db, WebhookEventTypes.LabelDeleted, label, actor, l => new WebhookLabelDeletedData(l), ct);
        broadcaster.Publish(boardEvent);
    }

    // ── Attachment-family M2 emit helpers (#329) ─────────────────────────────────────
    //
    // Metadata only — the file bytes never enter the event. Same single board bell the attachment
    // sites always rang plus one webhook event. attachment.deleted is published AFTER the row is
    // removed, from the captured attachment object (its Payload length stays readable in memory).

    public static async Task PublishAttachmentCreatedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardAttachment attachment,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildAttachmentEventAsync(db, WebhookEventTypes.AttachmentCreated, attachment, actor, (a, card) => new WebhookAttachmentCreatedData(a, card), ct);
        broadcaster.Publish(boardEvent);
    }

    public static async Task PublishAttachmentDeletedAsync
    (
        BoardDbContext db,
        BoardEventBroadcaster broadcaster,
        CardAttachment attachment,
        BoardUser actor,
        CancellationToken ct
    )
    {
        var boardEvent = await BuildAttachmentEventAsync(db, WebhookEventTypes.AttachmentDeleted, attachment, actor, (a, card) => new WebhookAttachmentDeletedData(a, card), ct);
        broadcaster.Publish(boardEvent);
    }

    // The fat-CardSummary single-event core (#329). Resolves the summary + board slug +
    // current lane name, then stamps the envelope. dataFactory shapes the per-event-type
    // `data` block from the resolved summary and lane name.
    private static async Task<BoardEvent> BuildCardSummaryEventAsync
    (
        BoardDbContext db,
        string eventType,
        CardItem card,
        BoardUser actor,
        Func<CardSummary, string, object> dataFactory,
        CancellationToken ct
    )
    {
        var summary = await BuildSummaryAsync(db, card, ct);
        var (boardSlug, laneName) = await ResolveBoardSlugAndLaneNameAsync(db, card.BoardId, card.LaneId, ct);

        return BuildEvent(eventType, card.BoardId, boardSlug, actor, dataFactory(summary, laneName));
    }

    private static async Task<List<BoardEvent>> BuildCardSummaryEventBatchAsync
    (
        BoardDbContext db,
        string eventType,
        IReadOnlyList<CardItem> cards,
        BoardUser actor,
        Func<CardSummary, string, object> dataFactory,
        CancellationToken ct
    )
    {
        if (cards.Count == 0)
        {
            return [];
        }

        var summariesById = (await CardSummaryBuilder.BuildAsync(db, [.. cards], ct))
            .ToDictionary(summary => summary.Id);

        var boardIds = cards.Select(c => c.BoardId).Distinct().ToList();
        var slugByBoard = await db.Boards
            .Where(b => boardIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Slug, ct);

        var laneIds = cards.Select(c => c.LaneId).Distinct().ToList();
        var laneNameById = await db.Lanes
            .Where(l => laneIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        List<BoardEvent> events = [];
        foreach (var card in cards)
        {
            var summary = summariesById[card.Id];
            var boardSlug = slugByBoard.GetValueOrDefault(card.BoardId, string.Empty);
            var laneName = laneNameById.GetValueOrDefault(card.LaneId, string.Empty);

            events.Add(BuildEvent(eventType, card.BoardId, boardSlug, actor, dataFactory(summary, laneName)));
        }

        return events;
    }

    // The comment-event core: resolves the card's board + number and the comment author's name,
    // then stamps the envelope. dataFactory shapes the per-event-type `data` block.
    private static async Task<BoardEvent> BuildCommentEventAsync
    (
        BoardDbContext db,
        string eventType,
        CardComment comment,
        BoardUser actor,
        Func<WebhookCommentData, WebhookCardRef, object> dataFactory,
        CancellationToken ct
    )
    {
        var card = await db.Cards
            .Where(c => c.Id == comment.CardId)
                .Select(c => new { c.BoardId, c.Number })
                    .FirstOrDefaultAsync(ct);

        var boardId = card?.BoardId ?? Guid.Empty;
        var cardNumber = card?.Number ?? 0;
        var boardSlug = await ResolveBoardSlugAsync(db, boardId, ct);
        var authorName = await ResolveUserNameAsync(db, comment.UserId, ct);

        var commentData = new WebhookCommentData
        (
            comment.Id,
            comment.CardId,
            cardNumber,
            comment.ContentMarkdown,
            comment.UserId,
            authorName,
            comment.LastUpdatedAtUtc
        );

        return BuildEvent(eventType, boardId, boardSlug, actor, dataFactory(commentData, new WebhookCardRef(comment.CardId, cardNumber)));
    }

    // The label-event core: resolves the board slug and projects the label resource.
    private static async Task<BoardEvent> BuildLabelEventAsync
    (
        BoardDbContext db,
        string eventType,
        Label label,
        BoardUser actor,
        Func<WebhookLabelData, object> dataFactory,
        CancellationToken ct
    )
    {
        var boardSlug = await ResolveBoardSlugAsync(db, label.BoardId, ct);
        var labelData = new WebhookLabelData(label.Id, label.BoardId, label.Name, label.Color);

        return BuildEvent(eventType, label.BoardId, boardSlug, actor, dataFactory(labelData));
    }

    // The attachment-event core: resolves the card's board + number and projects the attachment
    // metadata (SizeBytes is the stored payload length — the bytes never ride the wire).
    private static async Task<BoardEvent> BuildAttachmentEventAsync
    (
        BoardDbContext db,
        string eventType,
        CardAttachment attachment,
        BoardUser actor,
        Func<WebhookAttachmentData, WebhookCardRef, object> dataFactory,
        CancellationToken ct
    )
    {
        var card = await db.Cards
            .Where(c => c.Id == attachment.CardId)
                .Select(c => new { c.BoardId, c.Number })
                    .FirstOrDefaultAsync(ct);

        var boardId = card?.BoardId ?? Guid.Empty;
        var cardNumber = card?.Number ?? 0;
        var boardSlug = await ResolveBoardSlugAsync(db, boardId, ct);

        var attachmentData = new WebhookAttachmentData
        (
            attachment.Id,
            attachment.CardId,
            attachment.FileName,
            attachment.ContentType,
            attachment.Payload.Length,
            attachment.AddedByUserId,
            attachment.AddedAtUtc
        );

        return BuildEvent(eventType, boardId, boardSlug, actor, dataFactory(attachmentData, new WebhookCardRef(attachment.CardId, cardNumber)));
    }

    private static BoardEvent BuildEvent
    (
        string eventType,
        Guid boardId,
        string boardSlug,
        BoardUser actor,
        object data
    ) =>
        new
        (
            eventType,
            Ulid.NewUlid().ToString(),
            DateTimeOffset.UtcNow,
            EventVersion,
            boardId,
            boardSlug,
            new BoardEventActor(actor.Id, actor.Name, actor.Role.ToString()),
            data
        );

    private static async Task<CardSummary> BuildSummaryAsync(BoardDbContext db, CardItem card, CancellationToken ct)
    {
        var summaries = await CardSummaryBuilder.BuildAsync(db, [card], ct);
        return summaries[0];
    }

    private static async Task<string> ResolveBoardSlugAsync(BoardDbContext db, Guid boardId, CancellationToken ct) =>
        await db.Boards
            .Where(b => b.Id == boardId)
                .Select(b => b.Slug)
                    .FirstOrDefaultAsync(ct)
        ?? string.Empty;

    private static async Task<string> ResolveUserNameAsync(BoardDbContext db, Guid userId, CancellationToken ct) =>
        await db.Users
            .Where(u => u.Id == userId)
                .Select(u => u.Name)
                    .FirstOrDefaultAsync(ct)
        ?? string.Empty;

    private static async Task<(string BoardSlug, string LaneName)> ResolveBoardSlugAndLaneNameAsync
    (
        BoardDbContext db,
        Guid boardId,
        Guid laneId,
        CancellationToken ct
    )
    {
        var boardSlug = await ResolveBoardSlugAsync(db, boardId, ct);
        var laneName = await db.Lanes
            .Where(l => l.Id == laneId)
                .Select(l => l.Name)
                    .FirstOrDefaultAsync(ct)
            ?? string.Empty;

        return (boardSlug, laneName);
    }
}

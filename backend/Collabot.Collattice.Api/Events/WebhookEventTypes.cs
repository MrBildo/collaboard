namespace Collabot.Collattice.Api.Events;

// The single source of truth for webhook event-type identifiers and event-selection semantics.
// v1 kept the two event strings as consts on WebhookEventFactory with an explicit note to
// promote them here when the registry landed; this is that promotion. Both the emit-sites
// (WebhookEventFactory) and the subscription event-selection validation (WebhookSubscriptionStore)
// reference this set, so a site cannot emit an event type a subscription cannot select, and a
// subscription cannot select an event type that never fires.
//
// Each family was added to this class and its `All` set as its emit-site was wired — so the
// invariant `selectable ≡ deliverable` holds at every step (an operator can only select an event
// type that actually delivers). The catalog grows as new board facts earn an event; do NOT
// re-assert it as "complete" (card.deleted was added after the initial families, and more may
// follow). User-account events are intentionally out of scope.
public static class WebhookEventTypes
{
    public const string CardCreated = "card.created";
    public const string CardMoved = "card.moved";
    public const string CardUpdated = "card.updated";
    public const string CardArchived = "card.archived";
    public const string CardRestored = "card.restored";

    // A card is hard-deleted (irreversible) — distinct from card.archived, which is the reversible
    // "left the board" transition. Fires from REST DELETE /cards/{id} and the admin prune delete.
    // Carries the card's state at occurrence (the row is gone after); see the outside report that
    // surfaced the gap: https://github.com/MrBildo/collattice/issues/402
    public const string CardDeleted = "card.deleted";

    // The card is the subject — the automation cares "this card was labeled X", consistent with
    // every other card.* event. (Distinct from a future label.* resource lifecycle: here the label
    // resource didn't change, the card's label-set did.)
    public const string CardLabeled = "card.labeled";
    public const string CardUnlabeled = "card.unlabeled";

    // The comment-resource lifecycle on a card — created / edited / deleted. Distinct from the
    // card.* events: the card row didn't change, one of its comments did. comment.deleted carries
    // the comment's state at occurrence (the row is gone after the delete).
    public const string CommentCreated = "comment.created";
    public const string CommentUpdated = "comment.updated";
    public const string CommentDeleted = "comment.deleted";

    // The label-resource lifecycle on a board — created / renamed-or-recolored / deleted. Distinct
    // from card.labeled / card.unlabeled, which report a card's label-SET changing; here the label
    // resource itself changed.
    public const string LabelCreated = "label.created";
    public const string LabelUpdated = "label.updated";
    public const string LabelDeleted = "label.deleted";

    // The attachment lifecycle on a card — added / removed. Metadata only on the wire, never the
    // file bytes. attachment.deleted carries the metadata at occurrence (the row is gone after).
    // The .created/.deleted pin keeps the resource-lifecycle naming uniform across the catalog,
    // and the subject is the card whose attachment-set changed.
    public const string AttachmentCreated = "attachment.created";
    public const string AttachmentDeleted = "attachment.deleted";

    // The lane lifecycle on a board. A lane carries only a name and a position, so the two mutations
    // are rename and reorder (not a generic .updated). lane.reordered carries the board's FULL new
    // left-to-right order and fires from BOTH the bulk reorder and a single-lane position move;
    // a single update_lane changing name AND position co-fires lane.renamed +
    // lane.reordered. lane.deleted carries the lane's state at occurrence (the row is gone after).
    public const string LaneCreated = "lane.created";
    public const string LaneRenamed = "lane.renamed";
    public const string LaneReordered = "lane.reordered";
    public const string LaneDeleted = "lane.deleted";

    // The card-size lifecycle on a board — structurally a twin of the lane family. A size carries
    // only a name and an ordinal, so the two mutations are rename and reorder (not a generic
    // .updated). size.reordered carries the board's FULL new order and fires from BOTH the bulk
    // reorder and a single-size ordinal move; a single update_size changing name AND ordinal
    // co-fires size.renamed + size.reordered. size.deleted carries the size's state at occurrence
    // (the row is gone after).
    public const string SizeCreated = "size.created";
    public const string SizeRenamed = "size.renamed";
    public const string SizeReordered = "size.reordered";
    public const string SizeDeleted = "size.deleted";

    // The board lifecycle. Board PATCH only changes the name (the slug is immutable), so the rename is
    // a distinct event, not a generic .updated. These are WEBHOOK-ONLY: board CRUD has no SSE
    // broadcast today, so emitting them must not ring a new board bell — they go straight to the
    // webhook sink, keeping the SSE wire byte-for-byte unchanged. board.deleted references a
    // now-deleted board (state at occurrence).
    public const string BoardCreated = "board.created";
    public const string BoardRenamed = "board.renamed";
    public const string BoardDeleted = "board.deleted";

    // "all current and future event types" — a subscription whose selection is the wildcard
    // receives every event. Validated as a standalone selection by the store and matched by
    // Matches() at drain; deliberately NOT a member of `All` (it is not itself an event type).
    public const string Wildcard = "*";

    // The test-delivery (ping) event type fired by the /test affordance (POST
    // /webhooks/subscriptions/{id}/test and the test_webhook MCP tool). DELIVERABLE-ONLY: a board
    // mutation never raises it, so it is deliberately NOT in `All` and not selectable. The GitHub
    // "ping" idiom an integrator recognizes cold.
    public const string Ping = "webhook.ping";

    // The selectable board-event types — what subscription event-selection validates against.
    // Ordinal because event-type identifiers are exact ASCII tokens.
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CardCreated,
        CardMoved,
        CardUpdated,
        CardArchived,
        CardRestored,
        CardDeleted,
        CardLabeled,
        CardUnlabeled,
        CommentCreated,
        CommentUpdated,
        CommentDeleted,
        LabelCreated,
        LabelUpdated,
        LabelDeleted,
        AttachmentCreated,
        AttachmentDeleted,
        LaneCreated,
        LaneRenamed,
        LaneReordered,
        LaneDeleted,
        SizeCreated,
        SizeRenamed,
        SizeReordered,
        SizeDeleted,
        BoardCreated,
        BoardRenamed,
        BoardDeleted,
    };

    // A selection entry is valid iff it names a known event type or is the wildcard.
    public static bool IsValidSelection(string eventType) =>
        eventType == Wildcard || All.Contains(eventType);

    // Does a subscription's selection match a fired event type? The wildcard matches everything.
    public static bool Matches(IEnumerable<string> selection, string eventType) =>
        selection.Contains(Wildcard, StringComparer.Ordinal)
        || selection.Contains(eventType, StringComparer.Ordinal);
}

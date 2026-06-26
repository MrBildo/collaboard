namespace Collaboard.Api.Events;

// The single source of truth for webhook event-type identifiers and event-selection semantics
// (#326). v1 kept the two event strings as consts on WebhookEventFactory with an explicit note to
// promote them here when the registry landed; this is that promotion. Both the emit-sites
// (WebhookEventFactory) and the subscription event-selection validation (WebhookSubscriptionStore)
// reference this set, so a site cannot emit an event type a subscription cannot select, and a
// subscription cannot select an event type that never fires.
//
// M1 declares only the two live event types. The full board-scoped catalog (~18 types) lands in
// M2, which extends this class and its `All` set per family as it wires each emit-site — so the
// invariant `selectable ≡ deliverable` holds at every milestone (an operator can only select an
// event type that actually delivers). (#326 B1 — live-events-only in M1.)
public static class WebhookEventTypes
{
    public const string CardCreated = "card.created";
    public const string CardMoved = "card.moved";

    // "all current and future event types" — a subscription whose selection is the wildcard
    // receives every event. Validated as a standalone selection by the store and matched by
    // Matches() at drain; deliberately NOT a member of `All` (it is not itself an event type).
    public const string Wildcard = "*";

    // The test-delivery (ping) event type fired by the /test affordance (#326 — POST
    // /webhooks/subscriptions/{id}/test and the test_webhook MCP tool). DELIVERABLE-ONLY: a board
    // mutation never raises it, so it is deliberately NOT in `All` and not selectable — the M1
    // selectable catalog stays the two live events (CardCreated, CardMoved). The GitHub "ping"
    // idiom an integrator recognizes cold.
    public const string Ping = "webhook.ping";

    // The selectable board-event types — what subscription event-selection validates against. M1 =
    // the live events only (B1). Ordinal because event-type identifiers are exact ASCII tokens.
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CardCreated,
        CardMoved,
    };

    // A selection entry is valid iff it names a known event type or is the wildcard.
    public static bool IsValidSelection(string eventType) =>
        eventType == Wildcard || All.Contains(eventType);

    // Does a subscription's selection match a fired event type? The wildcard matches everything.
    public static bool Matches(IEnumerable<string> selection, string eventType) =>
        selection.Contains(Wildcard, StringComparer.Ordinal)
        || selection.Contains(eventType, StringComparer.Ordinal);
}

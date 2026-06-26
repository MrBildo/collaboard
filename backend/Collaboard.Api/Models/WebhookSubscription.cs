namespace Collaboard.Api.Models;

// A registered webhook subscription (#326 — webhooks v2 registry). v1 delivered to a single
// operator-configured endpoint (Webhooks:Endpoint); v2 turns delivery into a table of N
// subscriptions, each with its own URL, optional HMAC secret, enabled-state, and event-selection.
// The dispatcher loads the enabled rows per drained event and fans out to those whose selection
// matches the event type.
//
// Bare Guid Id, exposed directly through the management surface — matches Label / Lane / CardSize.
// A ULID external id / AggregateRoot tier is a pattern this codebase does not carry; reject it.
public class WebhookSubscription
{
    public Guid Id { get; set; }

    // Operator label for the management UI ("n8n prod"). Optional.
    public string? Name { get; set; }

    // The dial-out target. RETURNED in the management read projection — it is operator-trust
    // config, not a secret. Every delivery to this URL passes the uniform SSRF connect guard.
    public string Url { get; set; } = string.Empty;

    // HMAC-SHA256 signing key. WRITE-ONLY at the API surface (never projected to a read response —
    // the store hands out `signed: bool`, never this value); plaintext at rest because it is a
    // symmetric key the server must replay to sign each delivery, so it cannot be hashed. Same
    // DB-file trust boundary as BoardUser.AuthKey one table over.
    public string? Secret { get; set; }

    // Per-subscription pause. The dispatcher ANDs this with the global Webhooks:Enabled master
    // switch — either being false suppresses delivery.
    public bool Enabled { get; set; } = true;

    // The event-selection: which event types this subscription receives (or the "*" wildcard).
    // Value-converted to a JSON TEXT column with a value comparer (BoardDbContext). Matched in CLR
    // memory at drain — the converter defeats SQL translation of a relational Contains, so the
    // dispatcher loads enabled rows and filters in memory. The store treats the selection as
    // replace-only (a fresh list is assigned on update), and the value comparer makes EF detect
    // edits correctly regardless.
    public IList<string> EventTypes { get; set; } = [];
}

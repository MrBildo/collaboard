using Collaboard.Api.Models;

namespace Collaboard.Api.Events;

// The per-event `data` payloads (#320). Both embed the existing CardSummary DIRECTLY
// (D2 — no parallel webhook-only card DTO to drift; the `version` envelope field is the
// release valve if CardSummary's shape ever changes). CardSummary is GUID-keyed and
// carries no board/lane names, so the payload enriches it with the resolved laneName.
//
// Both events nest the card under a `card` key (rather than flattening card fields to
// the data root) so the embed-CardSummary-directly rule holds without forking and the
// two event shapes stay structurally consistent — card.moved must nest (it carries
// from/to alongside the card), and card.created follows the same shape. The card is
// "state at occurrence", not "current state": a card.created therefore carries
// commentCount 0 / attachmentCount 0 / latestComment null (freshly created), which is
// correct, not a bug.

public sealed record WebhookCardCreatedData(CardSummary Card, string LaneName);

public sealed record WebhookCardMovedData
(
    CardSummary Card,
    string LaneName,
    WebhookLaneRef From,
    WebhookLaneRef To
);

// The from/to transition on card.moved. The lane change is the load-bearing axis (an
// automation wired to "card entered Ready → assign"); position is the incidental
// intra-lane ordinal, retained because the snapshot-before-mutate discipline keeps it
// cheap. `from` is captured BEFORE the move mutates the card.
public sealed record WebhookLaneRef(Guid LaneId, string LaneName, int Position);

// The webhook.ping test-delivery payload (#326). A minimal body so an integrator can confirm the
// endpoint is reachable, signs, and parses — carries the subscription id and a human-readable
// message, nothing board-scoped (a ping is not a board mutation).
public sealed record WebhookPingData(Guid SubscriptionId, string Message);

# API Reference

All endpoints are under `/api/v1/`. Authentication is via the `X-User-Key` header.

## Boards

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards | All | List all boards |
| GET | /boards/{idOrSlug} | All | Get board by ID or slug |
| POST | /boards | Admin | Create board |
| PATCH | /boards/{id} | Admin | Update board name |
| DELETE | /boards/{id} | Admin | Delete board (must have zero non-archive lanes) |

## Board-Scoped Resources

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards/{boardId}/board | All | Composite: lanes + cards + sizes |
| GET | /boards/{boardId}/lanes | All | List lanes |
| POST | /boards/{boardId}/lanes | Admin | Create lane |
| POST | /boards/{boardId}/lanes/reorder | Admin | Reorder all non-archive lanes. Body `{ laneIds }` — complete desired order; all-or-nothing |
| GET | /boards/{boardId}/cards | All | List cards (enriched: labels, sizes, comment/attachment counts). Returns a `{ items, totalCount, offset, limit }` paged envelope. Query params: `since`, `labelId`, `laneId`, `search`, `includeArchived` (default `false`), `offset` (default `0`), `limit` |
| POST | /boards/{boardId}/cards | All | Create card |
| GET | /boards/{boardId}/sizes | All | List card sizes (ordered by ordinal) |
| POST | /boards/{boardId}/sizes | Admin | Create size |
| GET | /boards/{boardId}/labels | All | List labels for a board |
| POST | /boards/{boardId}/labels | Admin | Create label |
| PATCH | /boards/{boardId}/labels/{id} | Admin | Update label name/color |
| DELETE | /boards/{boardId}/labels/{id} | Admin | Delete label + cleanup card assignments |

## By-ID Operations

| Resource | Endpoints |
|----------|-----------|
| Lanes | `GET /lanes/{id}`, `PATCH /lanes/{id}`, `DELETE /lanes/{id}` |
| Sizes | `GET /sizes/{id}`, `PATCH /sizes/{id}` (name/ordinal), `DELETE /sizes/{id}` (blocked if in use) |
| Cards | `GET /cards/{id}` (enriched detail), `PATCH /cards/{id}`, `DELETE /cards/{id}`, `POST /cards/{id}/reorder`, `POST /cards/{id}/archive`, `POST /cards/{id}/restore` |

## Users

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /users | All | List users |
| GET | /users/{id} | All | Get user |
| POST | /users | Admin | Create user |
| PATCH | /users/{id} | Admin | Update user |
| PATCH | /users/{id}/deactivate | Admin | Deactivate user |
| GET | /auth/me | All | Current user info |

## Comments, Attachments, Card Labels

| Resource | Endpoints |
|----------|-----------|
| Card Labels | `GET /cards/{id}/labels`, `POST /cards/{id}/labels` (validates same board), `DELETE /cards/{id}/labels/{labelId}` |
| Comments | `GET /cards/{id}/comments`, `POST /cards/{id}/comments`, `PATCH /comments/{id}`, `DELETE /comments/{id}` |
| Attachments | `GET /cards/{id}/attachments`, `POST /cards/{id}/attachments` (5 MB via MCP / 50 MB via REST), `GET /attachments/{id}`, `DELETE /attachments/{id}` |

## Search

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /search/cards?q=&limit= | All | Global cross-board search. Returns results grouped by board. Default limit 20, max 50. |

Search supports:
- Free text — matches card name and description (case-insensitive)
- Card number — prefix with `#` (e.g. `#37`) for exact match
- Plain number — matches card number OR text content

## SSE Events

| Path | Notes |
|------|-------|
| /boards/{boardId}/events | Per-board stream; mutations broadcast to all connected clients |

## MCP Endpoint

| Path | Notes |
|------|-------|
| /mcp | Streamable HTTP transport — 44 tools (boards, cards, lanes, sizes, labels, comments, attachments, archive, bulk operations, search, prune, webhooks) |

## Webhooks

Collaboard can POST a structured event to a URL of your choice whenever something happens on a board — a card created, moved, or labeled; a comment posted; a lane reordered; and more, across a [22-event catalog](#event-types) — so an external consumer (a workflow tool, a script, an agent) can react to board activity without polling. Delivery targets are managed as **subscriptions** — you can register more than one, each with its own URL, an optional signing secret, an enabled state, and a selection of which events it wants. For a guided walkthrough — creating a subscription, sending a test delivery, and the recursion guard you need before pointing one at anything that creates cards — see the [Webhooks Integration Guide](integrating-webhooks.md). For the global delivery settings (master switch, timeout, retries, and the private-network security control), see [Host Configuration](../README.md#webhooks).

Every webhook endpoint below requires an administrator-level key — either the **Administrator** or the **AgentAdministrator** role. A request from any other role receives `403`.

### Managing subscriptions

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /webhooks/subscriptions | Administrator / AgentAdministrator | List all subscriptions, each with on-read delivery metrics. Never returns secrets. |
| GET | /webhooks/subscriptions/{id} | Administrator / AgentAdministrator | One subscription. `404` if it doesn't exist. |
| POST | /webhooks/subscriptions | Administrator / AgentAdministrator | Create a subscription (body below). Returns the created subscription (`201`). |
| PATCH | /webhooks/subscriptions/{id} | Administrator / AgentAdministrator | Update a subscription; any omitted field is left unchanged (body below). |
| DELETE | /webhooks/subscriptions/{id} | Administrator / AgentAdministrator | Delete a subscription. Returns `{ "deleted": true }`. Its delivery-log history is kept. |
| POST | /webhooks/subscriptions/{id}/test | Administrator / AgentAdministrator | Send a synchronous test delivery (a `webhook.ping`) to this subscription and return the outcome. |

**Create body** (`POST /webhooks/subscriptions`):

```json
{
  "url": "https://automation.example.com/collaboard-hook",
  "events": ["card.created", "card.moved"],
  "secret": "a-long-random-shared-secret",
  "enabled": true,
  "name": "automation prod"
}
```

- `url` (required) — the dial-out target. Must be `http`/`https`, and must not resolve to a blocked private or internal address unless `Webhooks:AllowPrivateNetworkTargets` is set (see [Host Configuration](../README.md#webhooks)).
- `events` (required, non-empty) — the event types this subscription receives. Use the exact event-type strings (`card.created`, `card.moved`) or the single wildcard `"*"` to receive every event type, including ones added in future versions. An empty list is rejected; an unknown event type is rejected with the list of valid ones.
- `secret` (optional) — the HMAC signing key. **Write-only**: accepted here, never returned by any read. When set, this subscription's deliveries are signed (see [Signing](#signing)).
- `enabled` (optional, default `true`) — whether the subscription delivers.
- `name` (optional) — a label for your own reference.

**Update body** (`PATCH /webhooks/subscriptions/{id}`) — the same fields, all optional; an omitted field is unchanged. The secret follows a set / keep / clear rule:

- omit `secret` → the secret is **kept** unchanged (so you can edit the URL without re-sending the secret);
- `"secret": "new-value"` → the secret is **replaced**;
- `"clearSecret": true` → the secret is **removed**, and the subscription goes unsigned.

**Subscription shape** (returned by the list, get, create, and update endpoints):

```json
{
  "id": "8f1c…",
  "name": "automation prod",
  "url": "https://automation.example.com/collaboard-hook",
  "enabled": true,
  "events": ["card.created", "card.moved"],
  "signed": true,
  "successCount": 142,
  "failureCount": 3,
  "lastDeliveryStatus": "Succeeded",
  "lastDeliveryAtUtc": "2026-06-25T16:42:25.770Z"
}
```

The secret never appears — `signed` is `true` when a secret is set, `false` otherwise. The metric fields (`successCount`, `failureCount`, `lastDeliveryStatus`, `lastDeliveryAtUtc`) are computed from the delivery log at read time; a brand-new subscription reports zeros and nulls.

**Test delivery** (`POST /webhooks/subscriptions/{id}/test`) sends one `webhook.ping` event to the subscription through the exact same delivery path as a real event — same private-network guard, same signing — and returns the outcome directly:

```json
{ "success": true, "statusCode": 200, "error": null }
```

It is synchronous (you get the result of the one attempt, with no retry) and it records a row in the delivery log like any other delivery. A blocked or unreachable target comes back with `"success": false` and the reason in `error`.

### Observability endpoints

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /webhooks/deliveries | Administrator / AgentAdministrator | Persisted delivery attempts, newest first. Query params: `boardId` (filter to one board), `subscriptionId` (filter to one subscription), `offset` (default 0), `limit` (default 50, max 200). Returns a `PagedResult`. |
| GET | /webhooks/status | Administrator / AgentAdministrator | Global delivery posture and subscription counts — booleans and numbers only, never a secret or a URL. Answers "is delivery on, are private targets allowed, how many subscriptions exist?" when the delivery log is empty. |
| GET | /webhooks/event-types | Administrator / AgentAdministrator | The full selectable [event catalog](#event-types) with display metadata, grouped by family: `[{ family, label, events: [{ type, label, description }] }]`. The server-side source of truth a selection UI consumes, so it can never drift from what the backend emits and accepts. |

Each `deliveries` item:

```json
{
  "id": "3a2b…",
  "subscriptionId": "8f1c…",
  "eventId": "01J9ZQK8H6F4N3M2P7R5T8V0XW",
  "eventType": "card.created",
  "boardId": "f6fa6794-4bed-44d0-9656-de8080791302",
  "attempt": 1,
  "status": "Succeeded",
  "httpStatusCode": 200,
  "error": null,
  "attemptedAtUtc": "2026-06-25T16:42:25.770Z"
}
```

`subscriptionId` identifies which subscription the attempt was for; it is `null` for an attempt whose subscription was later deleted (the log outlives the subscription). `status` is `Succeeded` or `Failed`; `httpStatusCode` and `error` are populated on a failed attempt where there was a response or an error to record.

`GET /webhooks/status` response:

```json
{
  "enabled": true,
  "allowPrivateNetworkTargets": false,
  "subscriptionCount": 3,
  "enabledSubscriptionCount": 2
}
```

- `enabled` — the `Webhooks:Enabled` global master switch. When `false`, no subscription delivers.
- `allowPrivateNetworkTargets` — whether the `Webhooks:AllowPrivateNetworkTargets` override is on (see [Host Configuration](../README.md#webhooks)).
- `subscriptionCount` — how many subscriptions are registered.
- `enabledSubscriptionCount` — how many of those are individually enabled.

### Event types

A subscription receives an event only when its `events` selection includes that event type (or the wildcard `"*"`). Collaboard emits a **22-event catalog** covering the full board-scoped lifecycle, grouped into six families. The same catalog — with display labels and descriptions for a selection UI — is served by `GET /webhooks/event-types`.

| Family | Event | Fires when |
|--------|-------|------------|
| Cards | `card.created` | A card first comes into existence — via REST `POST /boards/{boardId}/cards`, MCP `create_card`, or when an interactive draft card is finalized. A draft (temp) card does **not** fire until it is finalized. |
| | `card.moved` | A card's lane changes through any successful non-archive mutation — the dedicated reorder/move paths, a `PATCH /cards/{id}` or `update_card` that sets `laneId`, or `bulk_update_cards` (one event per moved card). A change that doesn't move the card fires no `card.moved`. Archiving and restoring do **not** fire `card.moved` (they fire `card.archived` / `card.restored`). |
| | `card.updated` | A card's name, description, or size changes. |
| | `card.archived` | A card is archived. |
| | `card.restored` | A card is restored from the archive. |
| | `card.labeled` | A label is added to a card (one event per label). |
| | `card.unlabeled` | A label is removed from a card (one event per label). |
| Comments | `comment.created` | A comment is added to a card. |
| | `comment.updated` | A comment is edited. |
| | `comment.deleted` | A comment is deleted. |
| Labels | `label.created` | A label is created on a board. |
| | `label.updated` | A label is renamed or recolored. |
| | `label.deleted` | A label is deleted from a board. |
| Attachments | `attachment.created` | An attachment is added to a card. |
| | `attachment.deleted` | An attachment is removed from a card. |
| Lanes | `lane.created` | A lane is created on a board. |
| | `lane.renamed` | A lane is renamed. |
| | `lane.reordered` | A board's lanes are reordered. |
| | `lane.deleted` | A lane is deleted from a board. |
| Boards | `board.created` | A board is created. |
| | `board.renamed` | A board is renamed. |
| | `board.deleted` | A board is deleted. |

Two coverage rules are worth knowing:

- **Multi-axis edits co-fire.** A single `PATCH /cards/{id}` / `update_card` / `bulk_update_cards` that changes more than one thing emits one event per axis — a call that renames a card **and** moves it **and** changes its labels emits `card.updated` **+** `card.moved` **+** `card.labeled` / `card.unlabeled`. An unchanged axis emits nothing.
- **One `lane.reordered` per reorder.** Reordering a board's lanes emits a single `lane.reordered` carrying the board's full new order — never one event per lane. Both the bulk reorder and a single-lane position move emit this same shape.

`webhook.ping` is a separate event type that exists only for the [test-delivery endpoint](#managing-subscriptions): board activity never produces it, and a subscription can't select it.

### The envelope

Every event is a JSON object with a shared envelope and a per-event `data` block. The body is camelCase, byte-identical in field naming to the REST API. A full `card.created` body:

```json
{
  "event": "card.created",
  "eventId": "01J9ZQK8H6F4N3M2P7R5T8V0XW",
  "occurredAt": "2026-06-18T16:42:25.770Z",
  "version": "1",
  "boardId": "f6fa6794-4bed-44d0-9656-de8080791302",
  "boardSlug": "collaboard",
  "actor": { "userId": "52df8c11-2c9a-4d1e-8b3f-7a6e5d4c3b2a", "name": "Bill Wheelock", "role": "Administrator" },
  "data": {
    "card": {
      "id": "a1b2c3d4-5e6f-7a8b-9c0d-1e2f3a4b5c6d",
      "number": 321,
      "name": "Investigate flaky test",
      "descriptionMarkdown": "Repro steps...",
      "sizeId": "e5f6a7b8-1234-5678-9abc-def012345678",
      "sizeName": "M",
      "laneId": "b7c8d9e0-1111-2222-3333-444455556666",
      "position": 0,
      "createdByUserId": "52df8c11-2c9a-4d1e-8b3f-7a6e5d4c3b2a",
      "createdAtUtc": "2026-06-18T16:42:25.770Z",
      "lastUpdatedByUserId": "52df8c11-2c9a-4d1e-8b3f-7a6e5d4c3b2a",
      "lastUpdatedAtUtc": "2026-06-18T16:42:25.770Z",
      "labels": [],
      "commentCount": 0,
      "attachmentCount": 0,
      "isArchived": false,
      "latestComment": null
    },
    "laneName": "Inbox"
  }
}
```

| Field | Type | Notes |
|-------|------|-------|
| `event` | string | The event type — one of the catalog values above (e.g. `card.created`), or `webhook.ping` for a test delivery. |
| `eventId` | string (ULID) | Unique per event. Use it to deduplicate: delivery is at-least-once, so the same `eventId` may arrive more than once (on a retry). |
| `occurredAt` | string (ISO-8601 UTC) | When the fact happened, server-side. Sort on this for ordering — events may arrive out of order. |
| `version` | string | Contract version, currently `"1"`. New fields may be added within a version; a consumer must ignore unknown fields rather than reject the payload. |
| `boardId` | string (GUID) | The board the card belongs to. |
| `boardSlug` | string | The board's slug (e.g. `collaboard`) — the human-readable key to filter on without a lookup. |
| `actor` | object | Who caused the event: `{ userId, name, role }`. `role` is the role **name** (`Administrator`, `HumanUser`, `AgentUser`, `AgentAdministrator`), not a number. This is the field a consumer filters on to avoid an automation loop — see the [integration guide](integrating-webhooks.md). |
| `data` | object | The per-event payload — see below. |

The `data` block differs per family. The envelope above is identical for every event; only `data` changes. Each shape below is what rides under `data`.

#### Card events

The card family embeds the full card (the same `CardSummary` shape the REST card endpoints return) under `card`, plus the resolved `laneName` — `CardSummary` is GUID-keyed and carries no lane name, so the event resolves it for you.

- **`card.created`, `card.updated`, `card.archived`, `card.restored`** — `{ card, laneName }`. The card reflects state *at the moment of the event*: a freshly created card has `commentCount: 0`, `attachmentCount: 0`, and `latestComment: null` — correct, not a missing value. `card.archived` / `card.restored` carry the card as it sits in the archive lane (or its restored target lane).
- **`card.moved`** — `{ card, laneName, from, to }`, with the transition:

  ```json
  "data": {
    "card": { /* the full card, now in the target lane */ },
    "laneName": "Ready",
    "from": { "laneId": "...", "laneName": "Inbox", "position": 3 },
    "to":   { "laneId": "...", "laneName": "Ready", "position": 0 }
  }
  ```

  The lane change is the point of `card.moved`. `from`/`to` carry both the lane (id + name) and the position the card left and landed at.
- **`card.labeled`, `card.unlabeled`** — `{ card, laneName, label }`, where `label` is `{ id, name, color }` (color is nullable). The label that was added or removed is embedded so a consumer knows *which* one without a second fetch. One event per label.

#### Comment events

**`comment.created`, `comment.updated`, `comment.deleted`** — `{ comment, card }`:

```json
"data": {
  "comment": {
    "id": "…", "cardId": "…", "cardNumber": 321,
    "contentMarkdown": "Looks good, shipping.",
    "authorUserId": "…", "authorName": "Bill Wheelock",
    "lastUpdatedAtUtc": "2026-06-18T16:42:25.770Z"
  },
  "card": { "id": "…", "number": 321 }
}
```

The comment is the changed resource; the card rides as a thin `{ id, number }` ref. `authorUserId` / `authorName` are the comment's **own** author — when an admin edits or deletes someone else's comment, the editor is the envelope `actor` while the author stays the comment's author. `comment.deleted` carries the comment's state at occurrence (the row is gone after).

#### Label events

**`label.created`, `label.updated`, `label.deleted`** — `{ label }`, where `label` is `{ id, boardId, name, color }` (color is nullable). This is the label-*resource* lifecycle on a board — distinct from `card.labeled` / `card.unlabeled`, which report a *card's* label-set changing.

#### Attachment events

**`attachment.created`, `attachment.deleted`** — `{ attachment, card }`:

```json
"data": {
  "attachment": {
    "id": "…", "cardId": "…",
    "fileName": "screenshot.png", "contentType": "image/png",
    "sizeBytes": 51234,
    "addedByUserId": "…", "addedAtUtc": "2026-06-18T16:42:25.770Z"
  },
  "card": { "id": "…", "number": 321 }
}
```

**Metadata only — the file bytes never ride the wire.** `sizeBytes` is the stored payload length. `attachment.deleted` carries the metadata at occurrence (the row is gone after).

#### Lane events

- **`lane.created`, `lane.renamed`, `lane.deleted`** — `{ lane }`, where `lane` is `{ id, boardId, name, position }`. `lane.deleted` carries the lane's state at occurrence.
- **`lane.reordered`** — `{ lanes }`, the board's **full new left-to-right order**, each entry `{ id, name, position }`:

  ```json
  "data": {
    "lanes": [
      { "id": "…", "name": "Backlog", "position": 0 },
      { "id": "…", "name": "In Progress", "position": 1 },
      { "id": "…", "name": "Done", "position": 2 }
    ]
  }
  ```

  Both the bulk reorder and a single-lane position move emit this same full-order shape, so a consumer gets the resulting order directly with no reconstruction.

#### Board events

**`board.created`, `board.renamed`, `board.deleted`** — `{ board }`, where `board` is `{ id, slug, name }`. The envelope's `boardId` / `boardSlug` already identify the board; the embedded resource adds the `name` (and `slug` for completeness). `board.deleted` references a now-deleted board (state at occurrence).

#### Ping

**`webhook.ping`** (test deliveries only) — `{ subscriptionId, message }`, a minimal body so a receiver can confirm it is reachable, verifies the signature, and parses. A ping is not board-scoped, so its envelope carries an empty `boardId` / `boardSlug`.

### Delivery headers

Every POST carries these headers (signed or not):

| Header | Value |
|--------|-------|
| `Content-Type` | `application/json` |
| `User-Agent` | `Collaboard-Webhooks` |
| `X-Collaboard-Event` | The event type (`card.created`) — lets a consumer route without parsing the body. |
| `X-Collaboard-Delivery-Id` | The `eventId`. The delivery id is the event id, so every retry of one event carries the same value (which is what you want for dedup). |
| `X-Collaboard-Signature` | Present only when the subscription has a secret configured. |

### Signing

When a subscription has a secret set, every delivery to it carries:

```
X-Collaboard-Signature: sha256=<hex-lowercase-digest>
```

The digest is HMAC-SHA256 over the **exact raw bytes of the request body**, keyed by the shared secret. To verify, compute HMAC-SHA256 over the body you received (as received, before any re-parsing or re-serialization) with the same secret, and compare against the header value using a constant-time comparison. The `sha256=` prefix names the algorithm so a future scheme can change it without changing the header name.

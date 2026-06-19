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
| GET | /boards/{boardId}/cards | All | List cards (enriched: labels, sizes, comment/attachment counts). Filters: `since`, `labelId`, `laneId`, `search` |
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
| /mcp | Streamable HTTP transport — 39 tools (boards, cards, lanes, sizes, labels, comments, attachments, archive, bulk operations, search, prune) |

## Webhooks

Collaboard can POST a structured event to an operator-configured endpoint whenever a card is created or moved, so an external consumer (a workflow tool, a script, an agent) can react to board activity without polling. Webhooks are configured through host settings (see [Host Configuration](../README.md#webhooks)) — there is no API to manage them, so the only endpoints here are read-only diagnostics. For a guided walkthrough — turning it on, verifying a delivery, and the recursion-guard you need before pointing it at anything that creates cards — see the [Webhooks Integration Guide](integrating-webhooks.md).

### Observability endpoints

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /webhooks/deliveries | Admin | Persisted delivery attempts, newest first. Query params: `boardId` (filter to one board), `offset` (default 0), `limit` (default 50, max 200). Returns a `PagedResult`. |
| GET | /webhooks/status | Admin | Resolved configuration as booleans only — `{ enabled, endpointConfigured, signed }`. Never returns the secret or the endpoint URL. Answers "is it even on?" when the deliveries log is empty. |

`GET /webhooks/status` response:

```json
{ "enabled": true, "endpointConfigured": true, "signed": false }
```

- `enabled` — the `Webhooks:Enabled` master switch.
- `endpointConfigured` — whether `Webhooks:Endpoint` is set. With `enabled: true` and `endpointConfigured: false`, the feature is unconfigured; with `enabled: false`, delivery is paused.
- `signed` — whether a `Webhooks:Secret` is set (deliveries are HMAC-signed).

### Event types

| Event | Fires when |
|-------|------------|
| `card.created` | A card first comes into existence — via REST `POST /boards/{boardId}/cards`, MCP `create_card`, or when an interactive draft card is finalized. A draft (temp) card does **not** fire until it is finalized. |
| `card.moved` | A card's lane changes through any successful non-archive mutation — the dedicated reorder/move paths, a `PATCH /cards/{id}` or `update_card` that sets `laneId`, or `bulk_update_cards` (one event per moved card). A change that doesn't move the card (name/size/labels only) fires no `card.moved`. Archiving and restoring a card do **not** fire `card.moved`. |

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
| `event` | string | The event type — `card.created` or `card.moved`. |
| `eventId` | string (ULID) | Unique per event. Use it to deduplicate: delivery is at-least-once, so the same `eventId` may arrive more than once (on a retry). |
| `occurredAt` | string (ISO-8601 UTC) | When the fact happened, server-side. Sort on this for ordering — events may arrive out of order. |
| `version` | string | Contract version, currently `"1"`. New fields may be added within a version; a consumer must ignore unknown fields rather than reject the payload. |
| `boardId` | string (GUID) | The board the card belongs to. |
| `boardSlug` | string | The board's slug (e.g. `collaboard`) — the human-readable key to filter on without a lookup. |
| `actor` | object | Who caused the event: `{ userId, name, role }`. `role` is the role **name** (`Administrator`, `HumanUser`, `AgentUser`, `AgentAdministrator`), not a number. This is the field a consumer filters on to avoid an automation loop — see the [integration guide](integrating-webhooks.md). |
| `data` | object | The per-event payload — see below. |

**`card.created` `data`** — the full card (the same `CardSummary` shape the REST card endpoints return) under `card`, plus the resolved `laneName` alongside it. The card reflects state *at the moment it was created*: a freshly created card has `commentCount: 0`, `attachmentCount: 0`, and `latestComment: null`. That is correct, not a missing value.

**`card.moved` `data`** — the same `card` + `laneName`, plus the transition:

```json
"data": {
  "card": { /* the full card, now in the target lane */ },
  "laneName": "Ready",
  "from": { "laneId": "...", "laneName": "Inbox", "position": 3 },
  "to":   { "laneId": "...", "laneName": "Ready", "position": 0 }
}
```

The lane change is the point of `card.moved` — "card entered lane X" is the canonical trigger. `from`/`to` carry both the lane (id + name) and the position the card left and landed at.

### Delivery headers

Every POST carries these headers (signed or not):

| Header | Value |
|--------|-------|
| `Content-Type` | `application/json` |
| `User-Agent` | `Collaboard-Webhooks` |
| `X-Collaboard-Event` | The event type (`card.created`) — lets a consumer route without parsing the body. |
| `X-Collaboard-Delivery-Id` | The `eventId`. In v1 the delivery id is the event id, so every retry of one event carries the same value (which is what you want for dedup). |
| `X-Collaboard-Signature` | Present only when a `Webhooks:Secret` is configured. |

### Signing

When a `Webhooks:Secret` is set, every delivery carries:

```
X-Collaboard-Signature: sha256=<hex-lowercase-digest>
```

The digest is HMAC-SHA256 over the **exact raw bytes of the request body**, keyed by the shared secret. To verify, compute HMAC-SHA256 over the body you received (as received, before any re-parsing or re-serialization) with the same secret, and compare against the header value using a constant-time comparison. The `sha256=` prefix names the algorithm so a future scheme can change it without changing the header name.

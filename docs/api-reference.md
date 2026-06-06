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
| /mcp | Streamable HTTP transport — 38 tools (boards, cards, lanes, sizes, labels, comments, attachments, archive, bulk operations, search, prune) |

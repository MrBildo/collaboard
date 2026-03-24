# API Surface

All endpoints under `/api/v1/`:

## Boards (admin-only for mutation)

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards | All | List all boards |
| GET | /boards/{idOrSlug} | All | Get board by Guid or slug |
| POST | /boards | Admin | Create board (name required, slug auto-derived, immutable) |
| PATCH | /boards/{id} | Admin | Update name only (slug unchanged) |
| DELETE | /boards/{id} | Admin | Delete board (must have zero non-archive lanes). Returns `{ deleted, archivedCardsDeleted }` if archived cards exist |

## Board-scoped resources

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards/{boardId}/board | All | Composite: lanes + cards + sizes for a board |
| GET | /boards/{boardId}/lanes | All | List lanes for a board |
| POST | /boards/{boardId}/lanes | Admin | Create lane in a board |
| GET | /boards/{boardId}/sizes | All | List card sizes for a board (ordered by ordinal) |
| POST | /boards/{boardId}/sizes | Admin | Create size in a board (auto-ordinal if omitted) |
| GET | /boards/{boardId}/cards | All | List cards (enriched: labels, sizeId, sizeName, commentCount, attachmentCount, isArchived). Returns `{ items, totalCount, offset, limit }` paged envelope. Optional query params: `since` (DateTimeOffset), `labelId` (Guid), `laneId` (Guid), `includeArchived` (bool, default false), `offset` (int, default 0), `limit` (int, optional, max 200 — omit for all results) |
| POST | /boards/{boardId}/cards | All | Create card in a board (accepts `sizeId`, defaults to lowest-ordinal size). Card numbers are board-scoped (each board starts at 1 independently) |
| GET | /boards/{boardId}/labels | All | List labels for a board |
| POST | /boards/{boardId}/labels | Admin | Create label in a board |
| PATCH | /boards/{boardId}/labels/{id} | Admin | Update label name/color |
| DELETE | /boards/{boardId}/labels/{id} | Admin | Delete label + cleanup card assignments |

## By-ID operations (flat, resource knows its board)

| Resource | Endpoints |
|----------|-----------|
| Lanes | `GET /lanes/{id}`, `PATCH /lanes/{id}` (400 if archive lane; rejects `int.MaxValue` position), `DELETE /lanes/{id}` (400 if archive lane) |
| Sizes | `GET /sizes/{id}`, `PATCH /sizes/{id}` (name/ordinal), `DELETE /sizes/{id}` (blocked if in use by cards) |
| Cards | `GET /cards/{id}` (enriched: card, sizeName, user names, comments, labels, attachments, isArchived), `PATCH /cards/{id}` (accepts `sizeId`; 400 if archived), `DELETE /cards/{id}`, `POST /cards/{id}/reorder` (400 if archived or target is archive lane), `POST /cards/{id}/archive` (all roles; 400 if already archived), `POST /cards/{id}/restore` (accepts `{ laneId }`; all roles; 400 if not archived) |

## Global resources (not board-scoped)

| Resource | Endpoints |
|----------|-----------|
| Users | `GET /users`, `GET /users/{id}`, `POST /users`, `PATCH /users/{id}`, `PATCH /users/{id}/deactivate`, `GET /auth/me` |
| Card Labels | `GET /cards/{id}/labels`, `POST /cards/{id}/labels` (validates label belongs to same board as card), `DELETE /cards/{id}/labels/{labelId}` |
| Comments | `GET /cards/{id}/comments`, `POST /cards/{id}/comments` (400 if archived), `PATCH /comments/{id}` (400 if archived), `DELETE /comments/{id}` (400 if archived) |
| Attachments | `GET /cards/{id}/attachments`, `POST /cards/{id}/attachments` (400 if archived), `GET /attachments/{id}` (unrestricted), `DELETE /attachments/{id}` (400 if archived) |

## SSE Events

| Path | Notes |
|------|-------|
| /boards/{boardId}/events | Per-board stream; label mutations broadcast per-board, user mutations broadcast globally |

## MCP

| Path | Notes |
|------|-------|
| /mcp | Streamable HTTP transport — 18 tools across SystemTools, BoardTools, CardTools, ArchiveTools, CommentTools, AttachmentTools, LabelTools |

**Tools (18):**
- **SystemTools:** `get_api_info` (returns base URL and API prefix for direct REST calls)
- **BoardTools:** `get_boards`, `get_lanes` (boardId required, includes cardCount per lane; excludes archive lanes), `get_sizes` (boardId required, ordered by ordinal)
- **CardTools:** `create_card` (supports labelIds, sizeId/sizeName — defaults to lowest-ordinal size; positions at top of lane; blocks archive lane), `move_card` (index optional; blocks to/from archive lane), `update_card` (supports laneId/index move, sizeId/sizeName, labelIds replace, no-op guard; blocks archived cards), `get_cards` (enriched: labels, sizeId, sizeName, commentCount, attachmentCount, isArchived; returns `{ items, totalCount, offset, limit }` paged envelope; `offset` param default 0, `limit` param default 200, max 500; `includeArchived` param default false), `get_card` (enriched: sizeName, attachments, user names, isArchived; supports cardNumber lookup)
- **ArchiveTools:** `archive_card` (all roles; moves card to archive lane), `restore_card` (all roles; requires laneId; moves card from archive to target lane)
- **CommentTools:** `add_comment` (blocks archived cards), `delete_comment` (blocks archived cards)
- **AttachmentTools:** `upload_attachment` (5MB limit, base64; blocks archived cards), `download_attachment` (returns base64 content), `delete_attachment` (blocks archived cards)
- **LabelTools:** `get_labels`, `add_label_to_card` (supports labelName; blocks archived cards), `remove_label_from_card` (supports labelName; blocks archived cards)

**Cross-cutting:** Card numbers are **board-scoped** (unique per board, not globally). All card-scoped tools accept `cardNumber` (long) as alternative to `cardId` (Guid), but **`cardNumber` requires `boardId` or `boardSlug`** — no fallback to global lookup. Label assignment tools accept `labelName` as alternative to `labelId`. Size tools accept `sizeName` as alternative to `sizeId`. Shared resolution via `McpCardResolver`.

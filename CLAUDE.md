# Collaboard

Kanban board web application — .NET Minimal API backend + React SPA frontend. Designed for both human users and AI agent collaboration via MCP tooling.

## Tech Stack

**Backend**
- .NET 10 / C# — ASP.NET Minimal API (Program.cs + endpoint group classes in `Endpoints/`)
- Entity Framework Core — SQLite provider
- Auth: custom header-based (`X-User-Key` only), no ASP.NET auth middleware

**Frontend**
- React 18 + TypeScript
- Vite (dev server + build)
- React Router v6 — routes: `/`, `/boards/:slug`, `/boards/:slug/cards/:cardNumber`
- Tailwind CSS v3 + shadcn/ui
- TanStack Query (data fetching)
- dnd-kit (drag-and-drop)
- react-markdown (markdown rendering)
- Axios (HTTP client)

**Testing**
- xUnit + Shouldly — integration tests via WebApplicationFactory + in-memory SQLite
- Arrange-Act-Assert pattern
- Test file naming: `*.Tests.cs`

**Orchestration**
- Aspire 13.1 (AppHost + ServiceDefaults)
- OpenTelemetry (logging, tracing, metrics via service defaults)
- Aspire Dashboard (dev-time observability)

## Build & Run

### Prerequisites
- .NET 10 SDK
- Node.js 22+
- Docker Desktop (for Aspire orchestration)
- Aspire CLI (optional): `irm https://aspire.dev/install.ps1 | iex`

### Local Development (Recommended)
```powershell
dotnet run --project backend/Collaboard.AppHost
```
Launches both API and frontend with the Aspire dashboard. The dashboard URL is printed to the console on startup — it provides structured logs, traces, metrics, and resource management.

The API gets a dynamic port (no more hardcoded 58343). The frontend gets a dynamic port. Aspire handles service discovery between them.

Optionally configure `Admin:AuthKey` in `appsettings.Development.json` in `backend/Collaboard.Api/` — otherwise a random key is generated and logged on first run.

### Tests
```powershell
cd backend
dotnet test
```

### Frontend Only (no Aspire)
```powershell
cd frontend
npm install
npm run dev
```
Vite dev server on port 5173 with proxy to localhost:58343 (requires API running separately).

## Auth Model

Header-based authentication — no ASP.NET auth middleware:
- `X-User-Key` — per-user ULID auth key (stored in `BoardUser` entity), sole auth header
- Roles: `Administrator`, `HumanUser`, `AgentUser`
- `IsActive` flag on `BoardUser` — deactivated users get 401
- Admin seed: uses `Admin:AuthKey` from config if set, else generates ULID and logs it
- **Use `Results.StatusCode(403)` not `Results.Forbid()`** (no auth middleware registered)
- `AgentUser` cannot delete cards; can delete own comments and attachments
- All users see all boards — no board-level membership

## API Surface

All endpoints under `/api/v1/`:

### Boards (admin-only for mutation)

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards | All | List all boards |
| GET | /boards/{idOrSlug} | All | Get board by Guid or slug |
| POST | /boards | Admin | Create board (name required, slug auto-derived, immutable) |
| PATCH | /boards/{id} | Admin | Update name only (slug unchanged) |
| DELETE | /boards/{id} | Admin | Delete board (must have zero lanes) |

### Board-scoped resources

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards/{boardId}/board | All | Composite: lanes + cards + sizes for a board |
| GET | /boards/{boardId}/lanes | All | List lanes for a board |
| POST | /boards/{boardId}/lanes | Admin | Create lane in a board |
| GET | /boards/{boardId}/sizes | All | List card sizes for a board (ordered by ordinal) |
| POST | /boards/{boardId}/sizes | Admin | Create size in a board (auto-ordinal if omitted) |
| GET | /boards/{boardId}/cards | All | List cards (enriched: labels, sizeId, sizeName, commentCount, attachmentCount). Returns `{ items, totalCount, offset, limit }` paged envelope. Optional query params: `since` (DateTimeOffset), `labelId` (Guid), `laneId` (Guid), `offset` (int, default 0), `limit` (int, optional, max 200 — omit for all results) |
| POST | /boards/{boardId}/cards | All | Create card in a board (accepts `sizeId`, defaults to lowest-ordinal size). Card numbers are board-scoped (each board starts at 1 independently) |
| GET | /boards/{boardId}/labels | All | List labels for a board |
| POST | /boards/{boardId}/labels | Admin | Create label in a board |
| PATCH | /boards/{boardId}/labels/{id} | Admin | Update label name/color |
| DELETE | /boards/{boardId}/labels/{id} | Admin | Delete label + cleanup card assignments |

### By-ID operations (flat, resource knows its board)

| Resource | Endpoints |
|----------|-----------|
| Lanes | `GET /lanes/{id}`, `PATCH /lanes/{id}`, `DELETE /lanes/{id}` |
| Sizes | `GET /sizes/{id}`, `PATCH /sizes/{id}` (name/ordinal), `DELETE /sizes/{id}` (blocked if in use by cards) |
| Cards | `GET /cards/{id}` (enriched: card, sizeName, user names, comments, labels, attachments), `PATCH /cards/{id}` (accepts `sizeId`), `DELETE /cards/{id}`, `POST /cards/{id}/reorder` |

### Global resources (not board-scoped)

| Resource | Endpoints |
|----------|-----------|
| Users | `GET /users`, `GET /users/{id}`, `POST /users`, `PATCH /users/{id}`, `PATCH /users/{id}/deactivate`, `GET /auth/me` |
| Card Labels | `GET /cards/{id}/labels`, `POST /cards/{id}/labels` (validates label belongs to same board as card), `DELETE /cards/{id}/labels/{labelId}` |
| Comments | `GET /cards/{id}/comments`, `POST /cards/{id}/comments`, `PATCH /comments/{id}`, `DELETE /comments/{id}` |
| Attachments | `GET /cards/{id}/attachments`, `POST /cards/{id}/attachments`, `GET /attachments/{id}`, `DELETE /attachments/{id}` |

### SSE Events

| Path | Notes |
|------|-------|
| /boards/{boardId}/events | Per-board stream; label mutations broadcast per-board, user mutations broadcast globally |

### MCP

| Path | Notes |
|------|-------|
| /mcp | Streamable HTTP transport — 16 tools across SystemTools, BoardTools, CardTools, CommentTools, AttachmentTools, LabelTools |

**Tools (16):**
- **SystemTools:** `get_api_info` (returns base URL and API prefix for direct REST calls)
- **BoardTools:** `get_boards`, `get_lanes` (boardId required, includes cardCount per lane), `get_sizes` (boardId required, ordered by ordinal)
- **CardTools:** `create_card` (supports labelIds, sizeId/sizeName — defaults to lowest-ordinal size; positions at top of lane), `move_card` (index optional), `update_card` (supports laneId/index move, sizeId/sizeName, labelIds replace, no-op guard), `get_cards` (enriched: labels, sizeId, sizeName, commentCount, attachmentCount; returns `{ items, totalCount, offset, limit }` paged envelope; `offset` param default 0, `limit` param default 200, max 500), `get_card` (enriched: sizeName, attachments, user names; supports cardNumber lookup)
- **CommentTools:** `add_comment`
- **AttachmentTools:** `upload_attachment` (5MB limit, base64), `download_attachment` (returns base64 content), `delete_attachment`
- **LabelTools:** `get_labels`, `add_label_to_card` (supports labelName), `remove_label_from_card` (supports labelName)

**Cross-cutting:** Card numbers are **board-scoped** (unique per board, not globally). All card-scoped tools accept `cardNumber` (long) as alternative to `cardId` (Guid), but **`cardNumber` requires `boardId` or `boardSlug`** — no fallback to global lookup. Label assignment tools accept `labelName` as alternative to `labelId`. Size tools accept `sizeName` as alternative to `sizeId`. Shared resolution via `McpCardResolver`.

## .agents/ Directory Structure

Instance-local workspace (gitignored). See [[.agents/WORKFLOW]] for process. Run `/bootstrap` to create on fresh clone:

```
.agents/
├── roadmap/
│   └── INDEX.md              # Living backlog — what's next, ideas, decisions
├── specs/
│   └── TEMPLATE.md           # Spec template
├── kb/                       # Knowledge bases
├── research/                 # Research outputs
├── temp/                     # Scratch files — cleaned between milestones
├── archive/
│   ├── specs/                # Completed specs
│   ├── milestones/           # Milestone handoff docs
│   └── postmortems/          # Retrospectives
└── WORKFLOW.md               # Feature planning workflow
```

**Rules:**
1. No loose files — everything in designated folders
2. Specs are working set only; archive when merged to main
3. Roadmap (`INDEX.md`) is source of truth for future work
4. `temp/` is scratch — cleaned between milestones
5. Wikilink-style linking: `[[path/to/file]]` (no `.md` extension)
6. Run cleanup between milestones to enforce structure

## Conventions

### C# Style
- File-scoped namespaces (required)
- Primary constructors where appropriate
- No `sealed` as blanket convention — only use when inheritance would be genuinely harmful
- Pattern matching: `is null`, `is not null`
- No XML doc comments — comment only complex business logic
- `var` for all local variables where type is apparent
- Private fields: `_camelCase` with underscore prefix
- All other members: PascalCase
- Interfaces: `IPascalCase` (prefix with I)
- Guard clauses: `ArgumentNullException.ThrowIfNull()`
- Collection expressions: `[]` instead of `new List<>()`
- Expression-bodied members for one-liners
- `.editorconfig` enforced — run `dotnet format` before committing

### Endpoint Structure
- Endpoints are organized in static classes under `backend/Collaboard.Api/Endpoints/`
- Each resource has its own file: `BoardEndpoints.cs`, `UserEndpoints.cs`, etc.
- Extension methods on `RouteGroupBuilder` map to `api.MapXxxEndpoints()`
- Program.cs is a thin composition root (builder, services, middleware, endpoint registration)

### Frontend Style
- Functional components with hooks
- shadcn/ui components from `@/components/ui/`
- `cn()` utility for conditional classes (from `@/lib/utils`)
- TanStack Query for all API calls
- Axios instance from `@/lib/api`
- 2-space indentation

### Formatting
- `.editorconfig` is the source of truth for all formatting
- C#: 4 spaces, frontend: 2 spaces
- Null safety: non-nullable reference types, null violations are errors

### Git
- **NEVER** commit directly to main
- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`
- Squash merge to main
- All changes via feature branch + PR

### Testing
- xUnit with Shouldly assertions (WebApplicationFactory, real in-memory SQLite, no mocking)
- Arrange-Act-Assert pattern
- Test classes per resource: `*EndpointTests.Tests.cs`
- Shared infrastructure: `Infrastructure/CollaboardApiFactory.cs`, `TestAuthHelper.cs`

## Collaboard (Kanban)

See [[COLLABOARD]] for board conventions, lanes, labels, sizes, and workflow.

Use `/release` to cut a new version — it waits for CI, creates a GitHub Release, monitors the publish workflow, and reports when artifacts are ready.

## Definition of Done

Before moving any card to Review or declaring work complete:

```powershell
# 1. Build — backend
cd backend
dotnet build

# 2. Build — frontend (typecheck + Vite build)
cd frontend
npm run build

# 3. Test — backend
cd backend
dotnet test

# 4. Test — frontend
cd frontend
npm run test

# 5. Lint — frontend
cd frontend
npm run lint
npm run format:check
```

**Runtime observation:** Feature must be observable in the running application. Launch full stack via `dotnet run --project backend/Collaboard.AppHost`. Backend changes must respond correctly via API. Frontend changes must render in the browser. MCP changes must be callable and return expected results. Aspire Dashboard provides structured logs, traces, and metrics.

Format with `dotnet format` (backend) and `npm run format` (frontend) before committing.

## Skills

Skills live in `.claude/skills/`. Each skill is self-documenting via its own `SKILL.md`.

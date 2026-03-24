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

### Aspire Lifecycle

Use the Aspire skill and MCP tools to manage the Aspire lifecycle (start, stop, check resources, read logs/traces). Use `list_resources` and `doctor` to verify state before taking action.

**Hot reload:** Don't restart Aspire for frontend-only changes — the frontend dev server picks up changes automatically via hot reload. Only restart when backend code changes need to be picked up. Unnecessary restarts waste time and change the port.

**File lock gotcha:** If Aspire is running and you need to build or test, kill the Aspire process first. The running API locks DLLs (e.g., `Collaboard.ServiceDefaults.dll`) and causes MSB3027 file copy errors. Before `dotnet test` or `dotnet build`, check for and kill any running Aspire/Collaboard.Api processes if the build fails with file lock errors.

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

## Archive Model

Cards can be **archived** — hidden from normal views but preserved for reference:
- Each board has a hidden **archive lane** (`Lane.IsArchiveLane = true`, `Position = int.MaxValue`)
- Archive lane is auto-created with each board and excluded from all lane listings
- Archived cards are cards whose `LaneId` points to an archive lane
- Archived cards are **fully frozen** — no edits, comments, labels, or attachment mutations (400 response)
- Only **restore** and **delete** are allowed on archived cards
- `ArchiveGuard.IsCardArchivedAsync(db, cardId)` — shared helper for archive checks
- All card responses include `isArchived` (bool) field
- Prune defaults to archive action (not delete)
- Search excludes archived cards unless `archiveBoardId` is specified

## API Surface

All endpoints under `/api/v1/`:

### Boards (admin-only for mutation)

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards | All | List all boards |
| GET | /boards/{idOrSlug} | All | Get board by Guid or slug |
| POST | /boards | Admin | Create board (name required, slug auto-derived, immutable) |
| PATCH | /boards/{id} | Admin | Update name only (slug unchanged) |
| DELETE | /boards/{id} | Admin | Delete board (must have zero non-archive lanes). Returns `{ deleted, archivedCardsDeleted }` if archived cards exist |

### Board-scoped resources

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

### By-ID operations (flat, resource knows its board)

| Resource | Endpoints |
|----------|-----------|
| Lanes | `GET /lanes/{id}`, `PATCH /lanes/{id}` (400 if archive lane; rejects `int.MaxValue` position), `DELETE /lanes/{id}` (400 if archive lane) |
| Sizes | `GET /sizes/{id}`, `PATCH /sizes/{id}` (name/ordinal), `DELETE /sizes/{id}` (blocked if in use by cards) |
| Cards | `GET /cards/{id}` (enriched: card, sizeName, user names, comments, labels, attachments, isArchived), `PATCH /cards/{id}` (accepts `sizeId`; 400 if archived), `DELETE /cards/{id}`, `POST /cards/{id}/reorder` (400 if archived or target is archive lane), `POST /cards/{id}/archive` (all roles; 400 if already archived), `POST /cards/{id}/restore` (accepts `{ laneId }`; all roles; 400 if not archived) |

### Global resources (not board-scoped)

| Resource | Endpoints |
|----------|-----------|
| Users | `GET /users`, `GET /users/{id}`, `POST /users`, `PATCH /users/{id}`, `PATCH /users/{id}/deactivate`, `GET /auth/me` |
| Card Labels | `GET /cards/{id}/labels`, `POST /cards/{id}/labels` (validates label belongs to same board as card), `DELETE /cards/{id}/labels/{labelId}` |
| Comments | `GET /cards/{id}/comments`, `POST /cards/{id}/comments` (400 if archived), `PATCH /comments/{id}` (400 if archived), `DELETE /comments/{id}` (400 if archived) |
| Attachments | `GET /cards/{id}/attachments`, `POST /cards/{id}/attachments` (400 if archived), `GET /attachments/{id}` (unrestricted), `DELETE /attachments/{id}` (400 if archived) |

### SSE Events

| Path | Notes |
|------|-------|
| /boards/{boardId}/events | Per-board stream; label mutations broadcast per-board, user mutations broadcast globally |

### MCP

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
7. Archive is append-only — never delete archived content.
8. Research is grouped — each effort gets its own subfolder in `research/`.

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
- **Delete branches after merge** — delete feature/bugfix branches on GitHub and locally after PRs are merged. Don't let stale branches accumulate. Use `gh pr merge --delete-branch` or delete manually.

#### Multi-Card Branch Strategy

For multi-card features (e.g., an archive system spanning cards #163-#170), use a parent feature branch:

1. Create a parent feature branch (e.g., `feature/card-archive`) from the release branch
2. Sub-card branches PR into the parent feature branch — not directly into the release branch
3. Create a PR for each feature branch into the release branch (not direct merges) so each feature can be reviewed independently
4. The final release branch -> main PR is separate and covers the full feature
5. Add a comment to the card with the PR link when creating PRs

#### Parallel Work Safety

Branches that touch overlapping files **must** be sequential, not parallel. Create each branch from the release branch after the previous one merges.

When planning parallel work, check file overlap first. Backend cards with different files can run in parallel. Frontend cards touching the same components must be sequenced. The dependency analysis step must drive execution order — not just be documented and ignored. If in doubt, sequence; the time saved by parallelism is lost to conflict resolution.

### Testing
- xUnit with Shouldly assertions (WebApplicationFactory, real in-memory SQLite, no mocking)
- Arrange-Act-Assert pattern
- Test classes per resource: `*EndpointTests.Tests.cs`
- Shared infrastructure: `Infrastructure/CollaboardApiFactory.cs`, `TestAuthHelper.cs`
- **No Playwright testing** — don't use Playwright MCP for visual testing unless the user specifically asks. Auth, dynamic Aspire ports, etc. make it unreliable. Rely on TypeScript checks, Vite builds, and backend tests for validation. Let the user do visual/browser testing.

## Agent Behavior Rules

**Safety over speed.** Optimize for safety, always. Move slow. Verify each step before moving to the next. Wait for user confirmation at natural checkpoints. Don't batch risky operations. The cost of a mistake far exceeds the time saved by going fast.

- **Do not auto-fix lint errors.** When any lint errors are encountered — GitHub CI, local eslint, dotnet format, or any other linter — do NOT automatically fix them. Stop, evaluate, summarize the issues to the user, and wait for instructions before making changes.
- **Ask, don't guess.** If stuck or unsure, report back rather than guessing. Max 3 follow-up rounds per task before escalating to user.

## UI Design Process

When designing UI features, create self-contained HTML mockup files for user review before writing production code.

- Self-contained HTML with all CSS inline (no external dependencies)
- Match the project's exact CSS custom properties (copy from `styles.css` — dark/light theme vars, brand colors, border radius, etc.)
- Use phone frames (375x720px) for mobile mockups, desktop frames for desktop
- Show before/after or multiple states side-by-side (e.g., collapsed vs expanded)
- Include a "Design Notes" section at the bottom with implementation details
- Upload as card attachments when working on the board
- Save to `.agents/temp/` as working files

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

## Dispatching Work

### Dispatch Rules

- **Spec first:** Write specs to `.agents/specs/` before dispatching. No dispatch without a spec.
- Include ALL context the child needs — it has no memory of this session
- **Ask, don't guess:** Include: "If you get stuck or unsure, report back rather than guessing."
- Max 3 follow-up rounds per task before escalating to user
- Dispatch in parallel when independent, sequentially when dependent

### Sub-Agent Conventions

When dispatching coding or evaluation sub-agents via the Agent tool:

- **Model:** Always use `model: "opus"` (Opus High)
- **Skills:** Instruct sub-agents to use skills appropriate to the task — e.g., dotnet-dev for C# tasks, typescript-dev for TypeScript. A research agent doesn't need coding skills.
- **Report format:** Every sub-agent must return a standardized report. Include this template in the prompt:

    ```
    Return your findings in this standardized format:

    ## Report: <card or task title>

    ### Summary
    <1-2 sentence verdict>

    ### Deliverable Status
    | Deliverable | Status | Notes |
    |---|---|---|
    | <item> | Done / Partial / Missing | <detail> |

    ### Verification
    - Backend build: <pass/fail/not run>
    - Backend tests: <pass/fail/not run — include count>
    - Frontend typecheck: <pass/fail/not run>
    - Frontend lint: <pass/fail/not run>
    - Frontend tests: <pass/fail/not run — include count>

    ### Files Touched
    - <path> — <created/modified/read> — <what changed>

    ### Gaps & Issues
    1. <issue description>

    ### Convention Violations
    <list or "None">

    ### Recommendation
    <next steps, move to Review, stays in Ready, etc.>
    ```

### Parallel Dispatch

**Partition by resource, not by task.** When dispatching parallel agents, group work by the files being touched — not by the card or task being worked on. Two cards that edit the same files must go to the same agent. Two cards that touch completely separate projects can go to separate agents. The rule: **if two agents could write to the same file, they must be the same agent.**

When multiple agents need the same repo simultaneously, use **git worktrees** for physically separate working directories.

```powershell
git worktree add ../<repo>-wt-<short-name> -b feature/<branch-name> <start-point>
```

Each worktree needs its own dependency install. The `.git` store is shared.

## Path Conventions

- **Relative paths in docs and specs.** Never hardcode absolute paths in committed files.
- **Absolute paths in scripts only** when referencing the script's own location.
- Reference other projects as `../<name>` (relative to repo root) in CLAUDE.md and runtime configs.

## Relationship to Other Projects

| Project | Path | Relationship |
|---------|------|-------------|
| **Collabot** | `../collabot` | Primary consumer — connects via MCP SSE for kanban operations |
| **Collabot TUI** | `../collabot-tui` | Indirect consumer via Collabot harness |
| **Ecosystem** | `../ecosystem` | Tracks work on the ecosystem board |
| **Research Lab** | `../lab` | Tracks investigations on the research-lab board |
| **Knowledge Base** | `../kb` | Tracks tasks on the knowledge-base board |

**Reference projects for conventions:** `../collabot` (primary — process and orchestration conventions), `../collabot-tui` (.NET conventions), `kindkatchapi` (production .NET reference).

## Skills

Use available skills proactively when the task matches — e.g., invoke dotnet-dev when writing C# or typescript-dev for TypeScript. Skills are declared in your session; no need to search directories.

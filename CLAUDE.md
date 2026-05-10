# Collaboard

Kanban board web application — .NET Minimal API backend + React SPA frontend. Designed for both human users and AI agent collaboration via MCP tooling.

## Repository Rules — Hard

These rules are non-negotiable. They apply to every agent, every dispatch, every commit.

1. **`.agents/*` and `.claude/*` are permanently gitignored. Local-only. No exceptions.** Agent workspaces, dispatch logs, specs, review artifacts, and harness state live here. **Never `git add --force` a path under `.agents/*` or `.claude/*`.** Not for "preserving review files." Not for "consistency with prior tracked files." Not for any reason. If a reviewer or planner produces an artifact (review doc, brief, spec) inside a worktree's `.agents/temp/`, copy it OUT to the main repo's `.agents/temp/` (also gitignored) before cleaning up the worktree. If you find tracked files under these paths, scrub via `git rm --cached -r .agents .claude` on a chore branch and PR to main.

2. **Specs live under `.agents/specs/` and are NOT part of the published source.** Source code comments must not reference spec documents (e.g., `// per .agents/specs/multi-board.md §6.2`). Cross-reference internal design via card numbers, GitHub issues, or inline rationale — anything an external reader of the published source can resolve.

3. **Persistence: project decisions live in tracked infra docs, not in auto-memory.** Auto-memory (`.claude/projects/.../memory/`) is ONLY for soft personal preferences. All project decisions, conventions, workflows, and hard rules go in `CLAUDE.md`, `COLLABOARD.md`, specs under `.agents/specs/`, or the `collaboard` skill (board protocol). If it's about how the project works, update the relevant infra doc — not memory.

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
- Aspire 13.3, OpenTelemetry
- AppHost + ServiceDefaults
- Aspire Dashboard (dev-time observability)

## Build & Run

### Prerequisites
- .NET 10 SDK
- Node.js 22+
- Docker Desktop (for Aspire orchestration)
- Aspire CLI (optional): `irm https://aspire.dev/install.ps1 | iex`

### Recommended: Aspire

```powershell
aspire start
```

Starts the API, frontend Vite dev server, and Aspire dashboard with OpenTelemetry. Use `aspire describe` to check resource status. The dashboard URL is printed to the console on startup — it provides structured logs, traces, metrics, and resource management.

Equivalent without the CLI:

```powershell
dotnet run --project backend/Collaboard.AppHost
```

The API gets a dynamic port (no more hardcoded 58343). The frontend gets a dynamic port. Aspire handles service discovery between them.

Optionally configure `Admin:AuthKey` in `appsettings.Development.json` in `backend/Collaboard.Api/` — otherwise a random key is generated and logged on first run.

Aspire does NOT run workloads natively on Linux. Use standalone `dotnet run` for Linux testing.

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
| /mcp | Streamable HTTP transport — 19 tools across SystemTools, BoardTools, CardTools, ArchiveTools, CommentTools, AttachmentTools, LabelTools |

**Tools (19):**
- **SystemTools:** `get_api_info` (returns base URL and API prefix for direct REST calls)
- **BoardTools:** `get_boards`, `get_lanes` (boardId required, includes cardCount per lane; excludes archive lanes), `get_sizes` (boardId required, ordered by ordinal)
- **CardTools:** `create_card` (supports labelIds, sizeId/sizeName — defaults to lowest-ordinal size; positions at top of lane; blocks archive lane), `move_card` (index optional; blocks to/from archive lane), `update_card` (supports laneId/index move, sizeId/sizeName, labelIds replace, no-op guard; blocks archived cards), `get_cards` (enriched: labels, sizeId, sizeName, commentCount, attachmentCount, isArchived; returns `{ items, totalCount, offset, limit }` paged envelope; `offset` param default 0, `limit` param default 200, max 500; `includeArchived` param default false), `get_card` (enriched: sizeName, attachments, user names, isArchived; supports cardNumber lookup)
- **ArchiveTools:** `archive_card` (all roles; moves card to archive lane), `restore_card` (all roles; requires laneId; moves card from archive to target lane)
- **CommentTools:** `add_comment` (blocks archived cards), `delete_comment` (blocks archived cards)
- **AttachmentTools:** `upload_attachment` (5MB limit, base64; blocks archived cards), `download_attachment` (returns base64 content), `delete_attachment` (blocks archived cards)
- **LabelTools:** `get_labels`, `add_label_to_card` (supports labelName; blocks archived cards), `remove_label_from_card` (supports labelName; blocks archived cards)

**Cross-cutting:** Card numbers are **board-scoped** (unique per board, not globally). All card-scoped tools accept `cardNumber` (long) as alternative to `cardId` (Guid), but **`cardNumber` requires `boardId` or `boardSlug`** — no fallback to global lookup. Label assignment tools accept `labelName` as alternative to `labelId`. Size tools accept `sizeName` as alternative to `sizeId`. Shared resolution via `McpCardResolver`.

## `.agents/` Workspace (gitignored)

Instance-local workspace. Run `/bootstrap` to create on a fresh clone. Layout:

```
.agents/
├── DISPATCH_LOG.md           # Project-scope dispatch log (Cora)
├── agents/<name>/            # Per-bot workspaces (LESSONS, JOURNAL, TODO, HANDOFF, archive/)
├── docs/                     # Internal team docs — TEAM.md, ONBOARDING.md
├── specs/                    # Architecture specs and feature specs (TEMPLATE.md included)
├── research/                 # Research outputs, grouped per effort
├── kb/                       # Knowledge bases
├── temp/                     # Scratch — design discussions, review artifacts, briefs
├── roadmap/INDEX.md          # Living backlog of ideas / decisions outside the board
└── archive/                  # Completed specs, milestone handoffs, post-mortems (append-only)
```

Workspace governance lives in the `agent-workspace` skill — not in this doc. Per-bot workspaces and how they're maintained are owned by each bot.

## Coding Conventions

### Skills agents must use

- **C# / .NET work:** invoke the `dotnet-dev` skill. Universal conventions live there.
- **TypeScript / React work:** invoke the `typescript-dev` skill. Universal conventions live there.

The skills carry universal patterns. The sections below name only Collaboard-specific overrides, project structure, and conventions that don't fit cleanly in skill scope.

### .NET overrides

- **`.editorconfig` is the source of truth** for formatting and analyzer severity. Don't override it; configure your editor to respect it. Don't modify it to work around conflicts either — restructure the code or use `#pragma` instead.
- **Run `dotnet format` before committing.**
- **Use `Results.StatusCode(403)` not `Results.Forbid()`** — there's no auth middleware registered, so `Results.Forbid()` throws at runtime.

### Endpoint structure

- Endpoints live in static classes under `backend/Collaboard.Api/Endpoints/`, one file per resource (`BoardEndpoints.cs`, `UserEndpoints.cs`, etc.).
- Extension methods on `RouteGroupBuilder` map to `api.MapXxxEndpoints()`.
- `Program.cs` is a thin composition root (builder + services + middleware + endpoint registration).

### Frontend overrides

- **shadcn/ui primitives only** — components from `@/components/ui/`. Never raw `<button>`, `<input>`, or `<dialog>` elements.
- **TanStack Query for all API calls.** No bare `fetch` or `axios` calls in components / hooks — go through `@/lib/api` (Axios instance).
- **`cn()` from `@/lib/utils`** for conditional classes.
- **Design system lives in `src/styles.css`.** Use semantic tokens (`bg-primary`, `text-foreground`, etc.). Never hardcoded colors. See [Frontend Design System](#frontend-design-system) below for the full token set.

### Testing

- **Backend:** xUnit + Shouldly via `WebApplicationFactory` + in-memory SQLite. No mocking. Arrange-Act-Assert. Test classes per resource: `*EndpointTests.Tests.cs`. Shared infrastructure: `Infrastructure/CollaboardApiFactory.cs`, `TestAuthHelper.cs`.
- **Frontend:** TypeScript typecheck + Vite build + `npm run test` cover correctness; lint + format:check cover style.
- **No Playwright testing** — don't use Playwright MCP for visual testing unless the operator specifically asks. Auth model + dynamic Aspire ports make it unreliable. Rely on tsc + Vite + backend tests; operator does visual/browser testing.

### Git

- **Never commit directly to main.** All changes via feature branch + PR.
- **Conventional commits:** `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`.
- **Branch naming:** `feature/`, `bugfix:`, `hotfix/`, `chore/`. Release branches: `release/<descriptive-name>` (never version numbers in release branch names — `release/v1.10.0` is wrong; `release/backend-v1` is right).
- **Squash merge to main via `gh pr merge --squash --delete-branch`.** Never local `git merge --squash` — it leaves PRs dangling open.
- **Delete branches after merge** — don't let stale branches accumulate.
- **Always merge PRs via `gh pr merge`**, never local merge.

#### Multi-card branch strategy

For multi-card features that span several PRs:

1. Cut a `release/<descriptive-name>` branch from main.
2. Each card cuts a feature branch off the release branch (or, for tightly-coupled work, off a parent feature branch).
3. Each card PRs into the release branch (not main directly) for independent review.
4. The final aggregate PR (`release/<name>` → `main`) is the operator's go/no-go gate on the whole feature.

#### Parallel work safety

Branches that touch overlapping files MUST serialize. Backend cards with disjoint files can run in parallel. Frontend cards on the same components must serialize. **If two agents could write to the same file, they must be the same agent.** The dependency check drives execution order — don't document it and ignore it. If in doubt, sequence; the time saved by parallelism is lost to conflict resolution.

## Agent Behavior Rules

**Safety over speed.** Optimize for safety, always. Move slow. Verify each step before moving to the next. Wait for user confirmation at natural checkpoints. Don't batch risky operations. The cost of a mistake far exceeds the time saved by going fast.

- **Do not auto-fix lint errors.** When any lint errors are encountered — GitHub CI, local eslint, dotnet format, or any other linter — do NOT automatically fix them. Stop, evaluate, summarize the issues to the user, and wait for instructions before making changes.
- **Ask, don't guess.** If stuck or unsure, report back rather than guessing. Max 3 follow-up rounds per task before escalating to user.

## Frontend Design System

### Colors

Use semantic Tailwind tokens, never hardcoded colors. The design system lives in `src/styles.css`.

```
bg-background    — page background
bg-card          — card/panel surfaces
bg-muted         — muted/recessed areas
bg-primary       — primary actions (cyan)
bg-accent        — highlights/badges (amber)
bg-destructive   — delete/error actions
text-foreground  — primary text
text-muted-foreground — secondary/helper text
border-border    — all borders
```

### Typography

- **Headings / UI labels:** Space Grotesk (font-sans via Tailwind)
- **Body / prose:** DM Sans (applied via prose contexts)
- Use Tailwind text utilities (`text-sm`, `text-base`, `text-lg`). No custom font sizes.

### Components

Always use shadcn/ui primitives from `@/components/ui/`. Never build raw HTML buttons, inputs, or dialogs.

- `<Button variant="default|outline|ghost|destructive" size="default|sm|lg">`
- `<Badge variant="default|secondary|outline">`
- `<Dialog>` / `<Sheet>` for modals and panels
- `<Input>`, `<Textarea>`, `<Select>` for form elements
- `<Tabs>`, `<Table>` for structured content
- `<Separator>` for visual dividers

### Cards

Kanban cards use: `rounded-lg shadow-sm border border-border bg-card p-3 hover:shadow-md transition-shadow`

### Icons

Use Lucide React (`lucide-react`). Import individual icons:
```tsx
import { Pencil, Trash2, Plus } from 'lucide-react';
```
Size: `className="w-4 h-4"` for inline, `"w-5 h-5"` for standalone.

### Dark Mode

Both light and dark themes are defined via CSS custom properties in `styles.css`. Dark mode activates via `data-theme="dark"` on the root element. Components should NOT use `dark:` Tailwind variants — the CSS vars handle everything automatically.

### Don'ts

- No inline styles except `image-rendering: pixelated` on the logo
- No `dark:` prefixed Tailwind classes — use the semantic tokens instead
- No new color values — if you need a color, it should come from the existing CSS vars
- No custom CSS classes — use Tailwind utilities composed via `cn()`
- No raw `<button>` or `<input>` elements — use shadcn components

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

Dispatch is coordinator scope (Cora). Detailed dispatch protocol — Pre-Dispatch Gate, model selection, token estimation, worktree discipline — lives in `~/.agents/roles/project-manager.md` (the role file) and project-anchored calibration lives in `.agents/agents/cora/LESSONS.md`. Project-canonical rules and the team report format are below.

### Dispatch rules (project-canonical)

- **Spec first.** Write specs to `.agents/specs/` before dispatching substantive work. No spec → no dispatch.
- **Full context in the prompt.** The child has no memory of the parent session — include every constraint, convention citation, and acceptance criterion the child needs to land the work without follow-up rounds.
- **Ask, don't guess.** Every dispatch prompt includes: *"If you get stuck or unsure, report back rather than guessing."* Max 3 follow-up rounds per task before escalating to the operator.
- **Sub-agents auto-load convention skills via frontmatter.** Don't restate skill invocations or verification commands in dispatch prompts — restating an incomplete list narrows the bot's work and creates regressions. Reference the canonical doc when needed (*"run the full verification suite per CLAUDE.md § Definition of Done"*).
- **Spec / architecture / review dispatches default to Opus.** Implementation defaults to Opus on meatier work; Sonnet for narrow refactors and pattern-following tasks. State the model explicitly on every dispatch.

### Standardized report format

Every dispatched coding or evaluation sub-agent returns findings in this format:

```
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
<next steps; move to Review, stays in Ready, etc.>
```

## Named Agents

| Agent | Role | Specialty |
|---|---|---|
| **Cora** | Operations coordinator | Board management, dispatch, design facilitation, release lifecycle, conventions stewardship |

Bots sign commits with `Co-Authored-By: <Name> <name>@collabot.dev>`. Cora's first session was 2026-05-09. As the team grows, additions land here.

## Path Conventions

- **Relative paths in committed docs and specs.** Never hardcode absolute paths in tracked files.
- **Absolute paths in scripts only** when referencing the script's own location.
- Reference sibling projects as `../<name>` (relative to repo root).

## Relationship to Other Projects

| Project | Path | Relationship |
|---|---|---|
| **Collabhost** | `../collabhost` | Peer project. Hosts the production deployment of Collaboard (and itself). Its own work is tracked on the `collabhost` Collaboard board. |
| **Collabot** | `../collabot` | Primary external consumer — connects via MCP for kanban operations. |
| **Collabot TUI** | `../collabot-tui` | Indirect consumer via Collabot harness. |
| **Ecosystem** | `../ecosystem` | Cross-project tooling and shared scripts. Its own work is tracked on the `ecosystem` Collaboard board. |
| **Research Lab** | `../lab` | Investigation workspace. Its own work is tracked on the `research-lab` Collaboard board. |
| **Knowledge Base** | `../kb` | Conventions and reference material. Its own work is tracked on the `knowledge-base` Collaboard board. |

Cross-project work that spans Collaboard + a peer (e.g., Collabhost) coordinates between named operations coordinators on each side (Cora here, Nolan on Collabhost). Externally-gated cards stay in Backlog with an explicit gate-and-trigger comment; the `Blocked` label is reserved for in-Triage gating.

## Skills

**Skill wins.** Universal craft (`dotnet-dev`, `typescript-dev`, `agent-workspace`, `collaboard`) lives in user-scope skills. Convention skills auto-load for craft bots via frontmatter. Project-specific overrides are documented above; if you find a pattern in this codebase that disagrees with the skill, the default assumption is "the skill is right, the codebase is behind" — not "the codebase reflects an intentional choice." Land the fix when you touch the surface; don't fix the world in one PR.

Available on demand at session start: `dotnet-dev`, `typescript-dev`, `do-dotnet-backend-architecture`, `agent-workspace`, `collaboard`, `use-github`, `aspire`, `tmux`. Invoke when relevant; bots that load convention skills via frontmatter will see them auto-injected.

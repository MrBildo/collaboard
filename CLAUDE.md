# Collaboard

## Identity

Kanban board web application — .NET Minimal API backend + React SPA frontend. Designed for both human users and AI agent collaboration via MCP tooling. Collaboard owns the board data model, REST API, MCP tools, and the React UI. It does NOT own agent orchestration (that's Collabot) or cross-project tooling (that's Ecosystem).

## Structure

```
collaboard/
├── CLAUDE.md                  # This file
├── op-docs/                   # Operational docs (overflow from CLAUDE.md)
│   ├── api-surface.md         # REST + MCP endpoint reference
│   ├── board-conventions.md   # Board lanes, labels, sizes, session workflow
│   ├── build-run.md           # Prerequisites, dev server, Aspire lifecycle
│   ├── conventions.md         # C#, frontend, endpoint, testing style rules
│   ├── dispatch.md            # Sub-agent dispatch protocol
│   └── workflow.md            # 9-step planning workflow
├── backend/
│   ├── Collaboard.Api/        # Minimal API (Endpoints/, Data/, Migrations/)
│   ├── Collaboard.AppHost/    # Aspire orchestration
│   ├── Collaboard.ServiceDefaults/
│   └── Collaboard.Tests/      # xUnit integration tests
├── frontend/                  # React + TypeScript SPA (Vite, Tailwind, shadcn/ui)
├── .agents/                   # Instance-local workspace (gitignored)
│   ├── roadmap/INDEX.md       # Living backlog
│   ├── specs/                 # Active specs (archive when merged)
│   ├── kb/                    # Knowledge bases
│   ├── research/              # Research outputs
│   ├── temp/                  # Scratch files
│   └── archive/               # Completed specs, milestones, postmortems
└── .agents.env                # Auth keys (gitignored)
```

### Rules

1. **No loose files.** Everything in designated folders.
2. **Specs before work.** Write specs to `.agents/specs/` before building anything non-trivial.
3. **Wikilink-style linking.** Cross-references use `[[path/to/file]]` syntax (no `.md` extension).
4. **Roadmap is source of truth** for future work (`INDEX.md`).
5. **Archive is append-only** — never delete archived content.

## Op-Docs & Knowledge Storage

Section headings with `[[op-docs/...]]` wikilinks point to detailed documentation. Read the relevant op-doc when working in that area — don't load all op-docs upfront.

**When you learn something new about this project** (workflows, conventions, technical decisions, gotchas, integration patterns):
1. Add it to the appropriate existing op-doc
2. If no appropriate op-doc exists, create one in `op-docs/` (kebab-case, topic-based name) and add a wikilink from the relevant CLAUDE.md section
3. Respect the 200-line budget per file — if an op-doc would exceed it, extract a sub-doc

**What goes where:**
- **Op-docs:** Operational knowledge, technical decisions, workflow rules, project conventions, architecture notes, gotchas
- **Auto-memory:** User communication preferences, soft behavioral preferences, user profile info — nothing else

**Do not** store operational knowledge in auto-memory. Do not create loose .md files at the project root — use `op-docs/`.

## Tech Stack

| Layer | Stack |
|-------|-------|
| Backend | .NET 10 / C# — ASP.NET Minimal API, EF Core + SQLite |
| Frontend | React 18 + TypeScript, Vite, Tailwind v3, shadcn/ui, TanStack Query, dnd-kit |
| Testing | xUnit + Shouldly (WebApplicationFactory + in-memory SQLite) |
| Orchestration | Aspire 13.1 (AppHost + ServiceDefaults, OpenTelemetry) |

## Build & Run — see [[op-docs/build-run]]

```powershell
dotnet run --project backend/Collaboard.AppHost   # Full stack via Aspire
cd backend && dotnet test                          # Backend tests
cd frontend && npm run build                       # Frontend typecheck + build
```

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
- Archived cards are **fully frozen** — no edits, comments, labels, or attachment mutations (400 response)
- Only **restore** and **delete** are allowed on archived cards
- `ArchiveGuard.IsCardArchivedAsync(db, cardId)` — shared helper for archive checks
- All card responses include `isArchived` (bool) field

## API Surface — see [[op-docs/api-surface]]

REST API under `/api/v1/` with 18 MCP tools. Covers boards, lanes, cards, sizes, labels, comments, attachments, users, SSE events, and archive operations. Card numbers are board-scoped. MCP accepts `cardNumber` + `boardSlug` as alternative to GUIDs.

## Conventions — see [[op-docs/conventions]]

C# file-scoped namespaces, primary constructors, `.editorconfig` enforced. Frontend: functional components, shadcn/ui, TanStack Query. See the op-doc for full style rules, endpoint structure, and testing conventions.

## UI Design Process

When designing UI features, create self-contained HTML mockup files for user review before writing production code:
- Self-contained HTML with all CSS inline (no external dependencies)
- Match the project's exact CSS custom properties (dark/light theme vars, brand colors)
- Use phone frames (375x720px) for mobile mockups, desktop frames for desktop
- Show before/after or multiple states side-by-side
- Upload as card attachments; save to `.agents/temp/` as working files

## Definition of Done

Before moving any card to Review or declaring work complete:

```powershell
cd backend && dotnet build              # 1. Backend build
cd frontend && npm run build            # 2. Frontend typecheck + Vite build
cd backend && dotnet test               # 3. Backend tests
cd frontend && npm run test             # 4. Frontend tests
cd frontend && npm run lint             # 5. Frontend lint
cd frontend && npm run format:check     # 6. Format check
```

**Runtime observation:** Feature must be observable in the running application. Launch full stack via `dotnet run --project backend/Collaboard.AppHost`. Backend changes must respond correctly via API. Frontend changes must render in the browser. MCP changes must be callable and return expected results.

Format with `dotnet format` (backend) and `npm run format` (frontend) before committing.

## Dispatching Work — see [[op-docs/dispatch]]

See [[op-docs/dispatch]] for full dispatch protocol including sub-agent report template and parallel dispatch rules.

## Board Conventions — see [[op-docs/board-conventions]]

Board slug: `collaboard`. See the op-doc for lanes, labels, sizes, card conventions, and session workflow.

Use `/release` to cut a new version — it waits for CI, creates a GitHub Release, monitors the publish workflow, and reports when artifacts are ready.

## Workflow — see [[op-docs/workflow]]

9-step planning workflow: Pre-Flight, Gather Context, Discuss Before Speccing, Write Spec, Implement, Verify, Review, Between Milestones, Board Stewardship.

## Skills

Use available skills proactively when the task matches — e.g., invoke dotnet-dev when writing C# or typescript-dev for TypeScript. Skills are declared in your session; no need to search directories.

## Agent Behavior Rules

**Safety over speed.** Optimize for safety, always. Move slow. Verify each step before moving to the next. Wait for user confirmation at natural checkpoints. Don't batch risky operations.

- **Do not auto-fix lint errors.** When lint errors are encountered — stop, evaluate, summarize to the user, and wait for instructions before making changes.
- **Ask, don't guess.** If stuck or unsure, report back rather than guessing. Max 3 follow-up rounds per task before escalating to user.

## Relationship to Other Projects

| Project | Path | Relationship |
|---------|------|-------------|
| **Collabot** | `../collabot` | Primary consumer — connects via MCP SSE for kanban operations |
| **Collabot TUI** | `../collabot-tui` | Indirect consumer via Collabot harness |
| **Ecosystem** | `../ecosystem` | Tracks work on the ecosystem board |
| **Research Lab** | `../lab` | Tracks investigations on the research-lab board |
| **Knowledge Base** | `../kb` | Tracks tasks on the knowledge-base board |

**Reference projects for conventions:** `../collabot` (primary — process and orchestration conventions), `../collabot-tui` (.NET conventions), `kindkatchapi` (production .NET reference).

## Git Rules

- **NEVER** commit directly to main
- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`
- Squash merge to main
- All changes via feature branch + PR
- **Delete branches after merge** — use `gh pr merge --delete-branch` or delete manually

## Path Conventions

- **Relative paths in docs and specs.** Never hardcode absolute paths in committed files.
- **Absolute paths in scripts only** when referencing the script's own location.
- Reference other projects as `../<name>` (relative to repo root) in CLAUDE.md and runtime configs.

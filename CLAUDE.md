# Collaboard

Kanban board web application — .NET Minimal API backend + React SPA frontend. Designed for both human users and AI agent collaboration via MCP tooling.

## Tech Stack

**Backend**
- .NET 10 / C# — ASP.NET Minimal API (single Program.cs)
- Entity Framework Core — SQLite provider
- Auth: custom header-based (`X-Api-Key` + `X-User-Key`), no ASP.NET auth middleware

**Frontend**
- React 18 + TypeScript
- Vite (dev server + build)
- Tailwind CSS v3 + shadcn/ui
- TanStack Query (data fetching)
- dnd-kit (drag-and-drop)
- react-markdown (markdown rendering)
- Axios (HTTP client)

**Testing**
- xUnit — integration tests via WebApplicationFactory + in-memory SQLite
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

Requires `appsettings.Development.json` in `backend/Collaboard.Api/` with `Security:ApiKey`.

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
- `X-Api-Key` — shared ULID secret (configured in appsettings, gitignored)
- `X-User-Key` — per-user ULID auth key (stored in `BoardUser` entity)
- Roles: `Administrator`, `HumanUser`, `AgentUser`
- **Use `Results.StatusCode(403)` not `Results.Forbid()`** (no auth middleware registered)
- `AgentUser` cannot delete cards or attachments

## API Surface

All endpoints under `/api/v1/`:

| Resource | Endpoints |
|----------|-----------|
| Board | `GET /board` |
| Users | `GET /users`, `POST /users` |
| Lanes | `POST /lanes`, `DELETE /lanes/{id}` |
| Cards | `POST /cards`, `PATCH /cards/{id}`, `DELETE /cards/{id}` |
| Comments | `POST /cards/{id}/comments`, `DELETE /comments/{id}` |
| Attachments | `POST /cards/{id}/attachments`, `GET /attachments/{id}`, `DELETE /attachments/{id}` |
| MCP | `GET /mcp` (stub manifest) |

## .agents/ Directory Structure

Instance-local workspace (gitignored). Run `/bootstrap` to create on fresh clone:

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
6. Run `/agents-tidy` between milestones to enforce structure

## Conventions

### C# Style
- File-scoped namespaces (required)
- Primary constructors where appropriate
- `sealed` on concrete classes
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
- xUnit with WebApplicationFactory (real in-memory SQLite, no mocking)
- Arrange-Act-Assert pattern
- Test classes per resource: `*EndpointTests.Tests.cs`
- Shared infrastructure: `Infrastructure/CollaboardApiFactory.cs`, `TestAuthHelper.cs`

## Known Issues

- `PATCH /cards` cannot set position to 0 or laneId to empty guid (sentinel value bug in partial update logic)
- `DELETE /comments` allows `AgentUser` — should restrict to own comments only
- `/mcp` endpoint is a stub manifest, not real MCP tool implementations

## Skills

| Skill | Purpose |
|-------|---------|
| `/agents-tidy` | Scan `.agents/` structure, enforce conventions |
| `/roadmap` | View and manage `.agents/roadmap/INDEX.md` |
| `/bootstrap` | Create `.agents/` directory structure on fresh clone |
| `/spec-discuss` | Collaborative spec development |
| `/post-mortem` | Structured retrospective |
| `/handoff` | Generate session handoff prompt |
| `/schema-sync` | Sync database schema snapshot to `.agents/docs/@schema-sync/` |

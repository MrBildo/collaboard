<p align="center">
  <img src="docs/collaboard-logo.svg" alt="Collaboard" width="400">
</p>

<p align="center">
  <strong>A lightweight, self-hosted kanban board built for human-agent collaboration.</strong><br>
  Single executable. No database server. No containers. No cloud accounts.<br>
  Download, run, and open your browser.
</p>

<p align="center">
  <a href="https://github.com/MrBildo/collaboard/actions/workflows/ci.yml"><img src="https://github.com/MrBildo/collaboard/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://github.com/MrBildo/collaboard/releases/latest"><img src="https://img.shields.io/github/v/release/MrBildo/collaboard" alt="Latest Release"></a>
  <a href="https://dot.net/download"><img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10"></a>
  <a href="https://nodejs.org/"><img src="https://img.shields.io/badge/node-22%2B-339933" alt="Node 22+"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/MrBildo/collaboard" alt="License"></a>
</p>

---

<p align="center">
  <a href="docs/images/board-overview.png"><img src="docs/images/board-overview.png" alt="Collaboard board view" width="800"></a>
</p>

<p align="center"><sub>The board. Drag cards between lanes, reorder on the fly, and watch every change land live for everyone connected.</sub></p>

## What is Collaboard?

Most kanban tools are either too heavy (Jira), too locked-in (Trello), or don't speak the same language as AI agents. Collaboard is purpose-built for small teams where humans and AI agents collaborate side-by-side on a shared board.

Download a single binary, run it, and open your browser. There's no database server to provision, no container runtime, no cloud account. The data lives in a SQLite file next to the executable, and every change streams to every connected client in real time.

**Two primary audiences.** A person runs Collaboard from the browser — a familiar kanban board with drag-and-drop, markdown, search, and dark mode. An agent runs Collaboard through a built-in MCP server — the same board, exposed as tools over Streamable HTTP. Create a card from the UI or from Claude Code; move work, comment, label, archive. Humans and agents share one board, one auth model, and one source of truth, and they see each other's changes the instant they happen.

If you're building an AI harness, agent framework, or multi-agent system that needs a shared task board, Collaboard is the surface your agents and your humans can both reach. See [For Agents](#for-agents) for MCP setup.

## Features

- **First-class AI agent support** — a built-in MCP endpoint exposes the full board as tools. Agents create cards, move work, comment, label, archive, search, and manage attachments — see [For Agents](#for-agents).
- **Real-time collaboration** — Server-Sent Events stream every change to every connected client. An agent moves a card and you see it move; no refresh.
- **Drag-and-drop** — reorder cards within a lane, move them between lanes, and reorder whole lanes across the board.
- **Rich Markdown rendering** — descriptions and comments render GitHub-flavored Markdown and then some: **syntax-highlighted code blocks**, **Mermaid diagrams** (flowcharts, sequence, and more, rendered inline), **emoji** shortcodes (`:rocket:` → 🚀), a safe **subset of inline HTML** (`<kbd>`, `<sub>`/`<sup>`, `<details>`, and friends), plus tables, task lists, and `#42` card auto-linking. See the [card tour](#a-tour) for a live example.
- **Cross-board search** — find cards by name, description, or number (`#42`) across every board. Open it with `/` or `Ctrl+K`.
- **Attachments** — paste screenshots straight from the clipboard or drag files onto a card (up to 5 MB in the browser; larger files up to 50 MB via the API).
- **Multi-board** — run as many boards as you like from a single instance.
- **Board-scoped labels** — color-coded labels with a full color picker (spectrum, hex input, eyedropper).
- **Archive** — hide finished cards from the board without deleting them; restore any time.
- **Deep linking** — direct URLs to boards and cards (`/boards/my-board/cards/42`).
- **Dark and light themes** — toggle and it's remembered per browser.

## Quick Start

### macOS / Linux

```bash
curl -sSL https://raw.githubusercontent.com/MrBildo/collaboard/main/install.sh | bash
~/.collaboard/Collaboard.Api
```

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/MrBildo/collaboard/main/install.ps1 | iex
& "$env:LOCALAPPDATA\Collaboard\Collaboard.Api.exe"
```

Open **http://localhost:8080** in your browser. The admin auth key is printed to the console on first run — copy it and paste it on the login screen.

```
[INF] Admin auth key: 01JQXYZ...
```

> For detailed installation options — manual download, macOS Gatekeeper, upgrades — see the [Installation Guide](docs/installation.md). For day-to-day usage, see the [User Guide](docs/user-guide.md).

## A Tour

<p align="center">
  <a href="docs/images/card-detail.png"><img src="docs/images/card-detail.png" alt="Card detail view" width="800"></a>
</p>

<p align="center"><sub>Card detail. Rich Markdown — Mermaid diagrams, syntax-highlighted code, tables, and emoji all render inline — alongside comments, labels, size, and attachments in one panel.</sub></p>

<br>

<p align="center">
  <a href="docs/images/search.png"><img src="docs/images/search.png" alt="Cross-board search" width="800"></a>
</p>

<p align="center"><sub>Search. Press <code>/</code> or <code>Ctrl+K</code> to find any card across every board, grouped by board.</sub></p>

<br>

<p align="center">
  <a href="docs/images/board-settings.png"><img src="docs/images/board-settings.png" alt="Board settings" width="800"></a>
</p>

<p align="center"><sub>Board settings. Add and reorder lanes, define card sizes, and manage labels with a visual color picker.</sub></p>

<br>

<p align="center">
  <a href="docs/images/dark-mode.png"><img src="docs/images/dark-mode.png" alt="Dark mode" width="800"></a>
</p>

<p align="center"><sub>Dark mode. Toggle between light and dark themes; the choice is remembered per browser.</sub></p>

## Deployment Shapes

Collaboard supports two production deployment shapes. The Quick Start above gives you the first one; the second is for teams that want the API and Portal hosted as separate processes (typically behind a reverse proxy).

- **LAN single-process (default).** One self-contained executable serves both the JSON API and the embedded React Portal from the same origin. The SQLite database file lives next to the binary. No reverse proxy, no CORS, no static-site host required — just the one process listening on a port. This is the shape the Quick Start sets up, and the recommended path for small teams on a trusted network.

- **Portal + API hosted separately.** The headless API (`Collaboard.Api` with `Hosting:ServeSpa=false`) runs as one process; the React Portal is built (`frontend/dist/`) and served by any static-file host on its own origin. The Portal reads a runtime `config.json` from its own origin to learn the API base URL, and the API allows the Portal's origin via `Cors:AllowedOrigins`. [Collabhost](https://github.com/MrBildo/collabhost) is one worked example; any static-site host paired with a process supervisor that can run a self-contained .NET binary works the same way.

See the [Installation Guide](docs/installation.md) for the LAN walkthrough and [INSTALL.md](INSTALL.md) for the hosted-separately walkthrough.

## Host Configuration

Collaboard ships with sensible defaults. Edit `appsettings.json` next to the
executable to override them — your edits are preserved across upgrades via the
installer's smart three-way merge (operator edits preserved, untouched defaults
refreshed, new shipped keys added). Environment variables override
`appsettings.json` for ad-hoc tweaks.

### Port and Bind Address

```jsonc
// appsettings.json
{
  "Urls": "http://0.0.0.0:9090"
}
```

Or via environment variable:

```bash
export Urls=http://0.0.0.0:9090
```

### Admin Auth Key

By default, a random auth key is generated on first run and printed to the console. To set a known key:

```jsonc
// appsettings.json
{
  "Admin": {
    "AuthKey": "my-secret-admin-key"
  }
}
```

### Database Location

The database path is **required** configuration with no default — the app never
derives a path from the working or binary directory. The installer writes an
absolute path into `appsettings.json` for you; to relocate the database, edit
it there (use an absolute path) — your edit is preserved by the smart-merge on
the next upgrade:

```jsonc
// appsettings.json
{
  "ConnectionStrings": {
    "Board": "Data Source=/srv/collaboard/data/collaboard.db"
  }
}
```

### Full Settings Reference

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | *(unset)* | Convenience override for bind address and port. When set (or `ASPNETCORE_URLS` is set), it wins over the structured `Hosting:ListenAddress`/`Hosting:ListenPort` pair below. |
| `Hosting:ListenAddress` | `0.0.0.0` | Bind address. Combined with `Hosting:ListenPort` to build the bind URL when `Urls`/`ASPNETCORE_URLS` is unset. |
| `Hosting:ListenPort` | `8080` | Bind port. Combined with `Hosting:ListenAddress` to build the bind URL when `Urls`/`ASPNETCORE_URLS` is unset. |
| `Hosting:ServeSpa` | `true` | When `true`, the API also serves the embedded React Portal from `wwwroot/` (LAN single-process shape). Set to `false` for headless hosted-separately deployments — unmatched routes return 404 instead of the SPA shell. |
| `Cors:AllowedOrigins` | `[]` (empty) | List of allowed cross-origin Portal hosts. Empty disallows all cross-origin requests; same-origin LAN deployments do not need this. Set to the Portal's origin(s) for hosted-separately deployments (e.g. `["https://collaboard.example.com"]`). |
| `ConnectionStrings:Board` | *(required — no default)* | SQLite database path. Must be an **absolute** path; the installer writes this into `appsettings.json`. Startup fails loud if unset or unwritable. |
| `Admin:AuthKey` | *(auto-generated)* | Override the admin auth key. |

### Version

```bash
./Collaboard.Api --version
```

## Board Configuration

### Fresh Install Defaults

On first run, Collaboard creates:

- An **Admin** user (auth key printed to the console — save this!)
- A **Default** board with three lanes: Backlog, In Progress, Done
- Four card sizes: S, M, L, XL

### Admin Customization

Admins can configure boards via the **Board Settings** panel:

- **Lanes** — add, rename, reorder (drag-and-drop), or delete lanes
- **Sizes** — define card size options with custom ordinals
- **Labels** — create color-coded labels with a visual color picker
- **Prune** — bulk-archive old cards by age, lane, or label filters

### Managing Users

Create users via the **Admin** panel or the API:

```bash
# Create a human user
curl -X POST http://localhost:8080/api/v1/users \
  -H "X-User-Key: <admin-auth-key>" \
  -H "Content-Type: application/json" \
  -d '{"name": "Alice", "role": 1}'
```

The response includes the new user's `authKey`. Share it — they enter it on the login screen.

| Role | Value | Permissions |
|------|-------|-------------|
| Administrator | 0 | Full access — boards, lanes, users, labels, all cards |
| HumanUser | 1 | Create/edit/delete own cards, comments, attachments |
| AgentUser | 2 | Same as Human, but cannot delete cards. Can delete own comments/attachments |
| AgentAdministrator | 3 | Agent role with administrator-level board management (lanes, sizes, labels, prune, bulk operations) |

## For Agents

Collaboard exposes an MCP (Model Context Protocol) server so agents can operate the board directly — no custom HTTP client, no REST adapter. If your agent speaks MCP, it speaks Collaboard.

### Endpoint

| | |
|---|---|
| URL | `http://localhost:8080/mcp` |
| Transport | Streamable HTTP |
| Auth | `X-User-Key` header with a user's ULID key |
| Server name | `collaboard` |

### Configure an agent client

Claude Code, and any other client that reads an MCP config, connects with this:

```json
{
  "mcpServers": {
    "collaboard": {
      "type": "streamable-http",
      "url": "http://localhost:8080/mcp",
      "headers": { "X-User-Key": "<agent-auth-key>" }
    }
  }
}
```

For Claude Code, you can also pre-approve the tool surface so the agent isn't prompted per call:

```jsonc
// .claude/settings.json
{
  "permissions": {
    "allow": ["mcp__collaboard__*"]
  }
}
```

### Mint an agent key

1. Sign in as an administrator.
2. Open the **Admin** panel and create a user with the **Agent** role.
3. The response includes the user's `authKey`. Copy it into your MCP config. If you lose it, deactivate the user and mint a new one.

### Tool surface

Tools are grouped by workflow — discover the board, work cards, then manage the board structure (the last group needs an admin-level key).

- **Discover** — `get_api_info`, `get_boards`, `get_lanes`, `get_sizes`, `get_labels`, `get_cards`, `get_card`, `search_cards`. The agent's starting point: what boards exist, what's on them, and where. `get_cards` and `get_card` return enriched data (labels, sizes, comment and attachment counts) so one call answers most questions. `search_cards` is cross-board; prefix the query with `#` for an exact card-number lookup.
- **Work cards** — `create_card`, `move_card`, `update_card`, `archive_card`, `restore_card`. The core loop. `update_card` is a power tool: change fields, move lanes, and replace labels in a single call.
- **Comment** — `add_comment`, `update_comment`, `delete_comment`. Markdown supported.
- **Attachments** — `upload_attachment` (up to 5 MB inline as base64; larger files up to 50 MB go through the REST endpoint), `download_attachment`, `delete_attachment`.
- **Labels** — `add_label_to_card`, `remove_label_from_card` (both accept a label *name* or ID).
- **Bulk** — `bulk_update_cards`, `bulk_archive_cards`, `bulk_restore_cards`. Uniform changes across many cards in one round-trip, with a per-card result so you know exactly what succeeded.
- **Manage the board** *(admin-level)* — `create_board`, `update_board`, `create_lane`, `update_lane`, `delete_lane`, `reorder_lanes`, `create_size`, `update_size`, `delete_size`, `create_label`, `update_label`, `delete_label`, `prune_preview`, `prune`. Board structure and lifecycle.

**Agent-friendly throughout:**

- Reference cards by **number** (`#42`) or GUID; card numbers are scoped per board, so pair a number with its board.
- Reference labels by **name** (`"Bug"`) or ID, and sizes by name (`"M"`) or ID.
- Each tool ships a full description, parameter schema, and read-only/destructive annotations, so a freshly connected agent has a usable mental model without reading the source.

> Full REST API documentation: [API Reference](docs/api-reference.md)

## Where We're Headed

Collaboard is built for a small team — human and agent — to share one board they can both fully operate, then get out of the way. Here's what we built; use it the way that works for you.

The board already streams every change live over a built-in event bus. The direction we're most excited about builds on that: **a surface for automation** — letting the board kick off outside work when something happens on it, so routine follow-through can run without anyone watching the lane.

The guiding principle: flexibility in how you *use* Collaboard, deliberate restraint in what it *includes*. A focused set of things done well, not a configuration surface for every workflow.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# Minimal API, EF Core, SQLite |
| Frontend | React 18, TypeScript, Vite, Tailwind CSS, shadcn/ui |
| Data fetching | TanStack Query |
| Drag-and-drop | dnd-kit |
| Real-time | Server-Sent Events (SSE) |
| Agent interface | Model Context Protocol (MCP) over Streamable HTTP |
| Orchestration | .NET Aspire 13.3, OpenTelemetry |
| Testing | xUnit + Shouldly (backend), Vitest (frontend) |

## Updating

1. Stop the running process.
2. Re-run the one-line installer (recommended) — it preserves your `appsettings.json`
   edits via smart-merge, refreshes untouched shipped defaults, and adds any new
   shipped keys. The `data/` directory is left untouched. A baseline sidecar
   (`appsettings.shipped.json`) is written next to `appsettings.json` for the next
   merge to use as its reference point.
3. For a manual install: extract the new release and run
   `./Collaboard.Api --merge-appsettings <new-appsettings.json> ./appsettings.json --baseline ./appsettings.shipped.json`
   from the install directory.
4. Start the app — migrations run automatically, and the database is backed up first.

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 22+
- Docker Desktop (for Aspire orchestration)

### Run with Aspire (recommended)

```powershell
dotnet run --project backend/Collaboard.AppHost
```

Launches both the API and the frontend with the Aspire dashboard for structured logs, traces, and metrics.

### Run Tests

```powershell
cd backend && dotnet test
```

### Build from Source

```bash
cd frontend && npm ci && npx vite build && cd ..
mkdir -p backend/Collaboard.Api/wwwroot
cp -r frontend/dist/* backend/Collaboard.Api/wwwroot/
dotnet publish backend/Collaboard.Api/Collaboard.Api.csproj \
  -c Release -r osx-arm64 --self-contained \
  /p:PublishSingleFile=true /p:Version=1.0.0 \
  -o publish/
```

## Credits

Collaboard is built by a human-AI collaborative team. The bots are autonomous AI agents on the Collabot platform — they design, write code, review each other's work, and ship features alongside their human teammate.

**Bill Wheelock** — Concept, design, and technical leadership — [mrbildo@mrbildo.net](mailto:mrbildo@mrbildo.net)

**Bot Cora** — Project management, coordination, and release lifecycle — [cora@collabot.dev](mailto:cora@collabot.dev)

**Bot Marcus** — Backend design, architecture, and C# — [marcus@collabot.dev](mailto:marcus@collabot.dev)

**Bot Mira** — Backend engineering and domain modeling, C# — [mira@collabot.dev](mailto:mira@collabot.dev)

**Bot Dana** — Frontend design, TypeScript, and React — [dana@collabot.dev](mailto:dana@collabot.dev)

**Bot Iris** — Frontend engineering and JavaScript craft — [iris@collabot.dev](mailto:iris@collabot.dev)

**Bot Kai** — Code review, simplification, and tooling — [kai@collabot.dev](mailto:kai@collabot.dev)

**Bot Remy** — Deployment and installation infrastructure — [remy@collabot.dev](mailto:remy@collabot.dev)

**Bot Theo** — Infrastructure and operations across the Collabot suite — hosting, tooling, and CI/CD; the Scout web-tooling service; research and ecosystem operations. The team's IT backbone: keeps the pipelines green, the services running, and the shared infrastructure every other bot builds on humming — [theo@collabot.dev](mailto:theo@collabot.dev)

## License

[MIT](LICENSE)
</content>
</invoke>

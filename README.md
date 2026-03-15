<p align="center">
  <img src="docs/collaboard-logo.png" alt="Collaboard" width="400">
</p>

<p align="center">
  A self-hosted kanban board for small teams.<br>
  Single executable. No database server. No containers. No cloud accounts.<br>
  Just download, run, and open your browser.
</p>

<p align="center">
  <a href="https://github.com/MrBildo/collaboard/actions/workflows/ci.yml"><img src="https://github.com/MrBildo/collaboard/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI"></a>
  <a href="https://github.com/MrBildo/collaboard/releases/latest"><img src="https://img.shields.io/github/v/release/MrBildo/collaboard" alt="Latest Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/MrBildo/collaboard" alt="License"></a>
</p>

---

Built for trusted LAN environments where a team needs a lightweight, collaborative task board with real-time updates.

## Features

- **First-class AI agent support** — Built-in [MCP](https://modelcontextprotocol.io/) endpoint with 13 purpose-built tools. Agents can create cards, move work between lanes, comment, label, and upload attachments — referencing cards by number (`#42`) or ID. Human-agent collaboration is a core design goal, not a bolt-on
- **Real-time for everyone** — SSE pushes every change to all connected clients instantly. When an agent moves a card or a teammate adds a comment, you see it live. No polling, no refresh
- **Full Markdown rendering** — Descriptions and comments render real Markdown, including **tables**, code blocks, headings, lists, and inline formatting. Not a stripped-down subset — actual GFM
- **Clipboard paste & drag-and-drop uploads** — Paste a screenshot or drag files directly onto a card. Attachments just work
- **Multi-board** — Create and manage multiple boards from a single instance
- **Drag-and-drop** — Reorder cards and move them between lanes with dnd-kit
- **Board-scoped labels** — Color-coded labels per board for categorization
- **Comments** — Threaded discussion on cards with full Markdown
- **Deep linking** — Direct URLs to boards and cards (`/boards/my-board/cards/42`)
- **Dark / light theme** — Toggle between themes, persisted per-browser
- **Single executable** — Self-contained binary with embedded SPA, no runtime dependencies
- **SQLite** — Zero-config database, auto-migrated on startup

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

Then open **http://localhost:8080** in your browser. Copy the admin auth key from the console output.

## Installation

### Download a Release

Download the latest release for your platform from [GitHub Releases](https://github.com/MrBildo/collaboard/releases/latest):

| Platform | Artifact |
|----------|----------|
| Windows 64-bit | `collaboard-win-x64.zip` |
| macOS Apple Silicon | `collaboard-osx-arm64.tar.gz` |
| macOS Intel | `collaboard-osx-x64.tar.gz` |
| Linux 64-bit | `collaboard-linux-x64.tar.gz` |
| Linux ARM64 | `collaboard-linux-arm64.tar.gz` |

Extract and run the executable. No runtime or framework installation required.

### macOS Gatekeeper

On macOS, you may need to remove the quarantine attribute after downloading:

```bash
xattr -d com.apple.quarantine ./Collaboard.Api
```

## Configuration

Collaboard ships with sensible defaults. All settings can be overridden via `appsettings.Local.json` (place next to the executable), environment variables, or command-line arguments.

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | `http://0.0.0.0:8080` | Bind address and port |
| `ConnectionStrings:Board` | `Data Source=./data/collaboard.db` | SQLite database path |
| `Admin:AuthKey` | *(auto-generated)* | Override the admin auth key |

### Environment Variables

Use double-underscore separators:

```bash
export COLLABOARD__Urls=http://0.0.0.0:9090
export COLLABOARD__Admin__AuthKey=my-secret-key
```

### Version

```bash
./Collaboard.Api --version
```

## API Reference

All endpoints are under `/api/v1/`. Authentication is via the `X-User-Key` header.

### Boards

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards | All | List all boards |
| GET | /boards/{idOrSlug} | All | Get board by ID or slug |
| POST | /boards | Admin | Create board |
| PATCH | /boards/{id} | Admin | Update board name |
| DELETE | /boards/{id} | Admin | Delete board (must have zero lanes) |

### Board-Scoped Resources

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards/{boardId}/board | All | Composite: lanes + cards |
| GET | /boards/{boardId}/lanes | All | List lanes |
| POST | /boards/{boardId}/lanes | Admin | Create lane |
| GET | /boards/{boardId}/cards | All | List cards (includes labels, comment/attachment counts). Filters: `since`, `labelId`, `laneId` |
| POST | /boards/{boardId}/cards | All | Create card |

### By-ID Operations

| Resource | Endpoints |
|----------|-----------|
| Lanes | `GET /lanes/{id}`, `PATCH /lanes/{id}`, `DELETE /lanes/{id}` |
| Cards | `GET /cards/{id}`, `PATCH /cards/{id}`, `DELETE /cards/{id}`, `POST /cards/{id}/reorder` |

### Users

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /users | All | List users |
| GET | /users/{id} | All | Get user |
| POST | /users | Admin | Create user |
| PATCH | /users/{id} | Admin | Update user |
| PATCH | /users/{id}/deactivate | Admin | Deactivate user |
| GET | /auth/me | All | Current user info |

### Labels (board-scoped)

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | /boards/{boardId}/labels | All | List labels for a board |
| POST | /boards/{boardId}/labels | Admin | Create label |
| PATCH | /boards/{boardId}/labels/{id} | Admin | Update label name/color |
| DELETE | /boards/{boardId}/labels/{id} | Admin | Delete label + cleanup card assignments |

### Comments, Attachments, Card Labels

| Resource | Endpoints |
|----------|-----------|
| Card Labels | `GET /cards/{id}/labels`, `POST /cards/{id}/labels` (validates same board), `DELETE /cards/{id}/labels/{labelId}` |
| Comments | `GET /cards/{id}/comments`, `POST /cards/{id}/comments`, `PATCH /comments/{id}`, `DELETE /comments/{id}` |
| Attachments | `GET /cards/{id}/attachments`, `POST /cards/{id}/attachments` (50MB max), `GET /attachments/{id}`, `DELETE /attachments/{id}` |

### SSE Events

| Path | Notes |
|------|-------|
| /boards/{boardId}/events | Per-board stream; global mutations broadcast to all streams |

## MCP for AI Agents

Collaboard is designed from the ground up for human-agent collaboration. The built-in MCP endpoint gives AI agents the same capabilities as human users — managing cards, commenting, labeling, and uploading files — all reflected in real-time to every connected client.

**13 tools** across boards, cards, comments, attachments, and labels:

| Category | Tools |
|----------|-------|
| Boards | `get_boards`, `get_lanes` |
| Cards | `create_card`, `move_card`, `update_card`, `get_cards`, `get_card` |
| Comments | `add_comment` |
| Attachments | `upload_attachment` (5MB base64), `delete_attachment` |
| Labels | `get_labels`, `add_label_to_card`, `remove_label_from_card` |

**Agent-friendly design:**
- Reference cards by **number** (`#42`) or GUID — every card-scoped tool accepts either
- Reference labels by **name** (`"Bug"`) or ID — no GUID lookup needed
- `get_cards` returns **enriched data** — labels, comment counts, and attachment counts per card
- `get_card` returns the full picture — card, comments with user names, labels, and attachment metadata in one call
- `create_card` and `update_card` accept **labels inline** — no separate calls needed
- `update_card` is a power tool — update fields, move lanes, and replace labels in a single operation

### Connecting

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

### Creating an Agent User

```bash
curl -X POST http://localhost:8080/api/v1/users \
  -H "X-User-Key: <admin-auth-key>" \
  -H "Content-Type: application/json" \
  -d '{"name": "My Agent", "role": "AgentUser"}'
```

Agent users can do everything human users can, except delete cards. They can delete their own comments and attachments.

## Admin Setup

On first run, Collaboard seeds:
- An **admin user** (auth key printed to console)
- A **Default** board with Backlog, In Progress, and Done lanes

### Creating Users

Use the admin auth key to create additional users:

```bash
# Create a human user
curl -X POST http://localhost:8080/api/v1/users \
  -H "X-User-Key: <admin-auth-key>" \
  -H "Content-Type: application/json" \
  -d '{"name": "Alice", "role": "HumanUser"}'
```

The response includes the new user's `authKey`. Share it with them — they'll enter it in the login screen.

### Roles

| Role | Permissions |
|------|-------------|
| Administrator | Full access — manage boards, lanes, users, labels |
| HumanUser | Create/edit/delete own cards, comments, attachments |
| AgentUser | Same as HumanUser, but cannot delete cards. Can delete own comments and attachments |

## Development

### Prerequisites

- .NET 10 SDK
- Node.js 22+
- Docker Desktop (for Aspire orchestration)

### Run with Aspire (recommended)

```powershell
dotnet run --project backend/Collaboard.AppHost
```

Launches both API and frontend with the Aspire dashboard for structured logs, traces, and metrics.

### Run Frontend Only

```bash
cd frontend
npm install
npm run dev
```

Vite dev server on port 5173 with proxy to the API.

### Run Tests

```powershell
# Backend
cd backend
dotnet test

# Frontend
cd frontend
npm test
```

### Build from Source

```bash
# Build frontend
cd frontend && npm ci && npx vite build && cd ..

# Copy to wwwroot
mkdir -p backend/Collaboard.Api/wwwroot
cp -r frontend/dist/* backend/Collaboard.Api/wwwroot/

# Publish self-contained executable
dotnet publish backend/Collaboard.Api/Collaboard.Api.csproj \
  -c Release -r osx-arm64 --self-contained \
  /p:PublishSingleFile=true /p:Version=0.1.0 \
  -o publish/
```

## Updating

1. Stop the running process
2. Download the new release for your platform
3. Replace the executable (keep your `data/` directory and `appsettings.Local.json`)
4. Start the app — migrations run automatically, database is backed up first

## License

MIT

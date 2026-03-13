# Collaboard

A single-repo Kanban suite with:

- **Backend**: ASP.NET Minimal API (.NET 10 preview), SQLite, ULID shared API key + per-user auth key, role-based entitlements.
- **Frontend**: React + Vite + TypeScript SPA with TanStack Query, Axios, Tailwind, and drag/drop interaction.
- **Agent Integration**: MCP metadata endpoint at `GET /mcp`.

## Repo layout

- `backend/Collaboard.Api` - API implementation
- `frontend` - React SPA
- `docker-compose.yml` - local orchestration

## Quick start

### Backend

```bash
cd backend/Collaboard.Api
dotnet run
```

Defaults:
- API base: `http://localhost:5000/api/v1`
- MCP manifest: `http://localhost:5000/mcp`

Create `backend/Collaboard.Api/appsettings.Development.json` (gitignored):

```json
{
  "Security": {
    "ApiKey": "<your-api-key>"
  }
}
```

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Create `frontend/.env` (gitignored):

```bash
VITE_API_KEY=<your-api-key>
VITE_USER_KEY=<user ulid>
```

## REST resources

- `/api/v1/board`
- `/api/v1/users`
- `/api/v1/lanes`
- `/api/v1/cards`
- `/api/v1/cards/{id}/comments`
- `/api/v1/cards/{id}/attachments`

Resource patterns are noun-based and use standard HTTP semantics (`GET`, `POST`, `PATCH`, `DELETE`) with appropriate response codes.

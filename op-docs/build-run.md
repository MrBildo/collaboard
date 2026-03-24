# Build & Run

## Prerequisites
- .NET 10 SDK
- Node.js 22+
- Docker Desktop (for Aspire orchestration)
- Aspire CLI (optional): `irm https://aspire.dev/install.ps1 | iex`

## Local Development (Recommended)
```powershell
dotnet run --project backend/Collaboard.AppHost
```
Launches both API and frontend with the Aspire dashboard. The dashboard URL is printed to the console on startup — it provides structured logs, traces, metrics, and resource management.

The API gets a dynamic port (no more hardcoded 58343). The frontend gets a dynamic port. Aspire handles service discovery between them.

Optionally configure `Admin:AuthKey` in `appsettings.Development.json` in `backend/Collaboard.Api/` — otherwise a random key is generated and logged on first run.

## Tests
```powershell
cd backend
dotnet test
```

## Aspire Lifecycle

Use the Aspire skill and MCP tools to manage the Aspire lifecycle (start, stop, check resources, read logs/traces). Use `list_resources` and `doctor` to verify state before taking action.

**Hot reload:** Don't restart Aspire for frontend-only changes — the frontend dev server picks up changes automatically via hot reload. Only restart when backend code changes need to be picked up. Unnecessary restarts waste time and change the port.

**File lock gotcha:** If Aspire is running and you need to build or test, kill the Aspire process first. The running API locks DLLs (e.g., `Collaboard.ServiceDefaults.dll`) and causes MSB3027 file copy errors. Before `dotnet test` or `dotnet build`, check for and kill any running Aspire/Collaboard.Api processes if the build fails with file lock errors.

## Frontend Only (no Aspire)
```powershell
cd frontend
npm install
npm run dev
```
Vite dev server on port 5173 with proxy to localhost:58343 (requires API running separately).

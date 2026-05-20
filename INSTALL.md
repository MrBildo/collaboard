# Collaboard — Setup Guide

## Quick Start

If you used the one-line installer (see [Installation Guide](docs/installation.md)),
`appsettings.json` is already seeded with an absolute database path — skip to step 2.

1. **Set the database path (required before first run).** Collaboard requires
   `ConnectionStrings:Board` to be an absolute path and does not derive one from
   the working or binary directory — startup fails loud if it is unset. Edit
   `appsettings.json` next to the executable to set an absolute path:

   ```jsonc
   // appsettings.json
   {
     "ConnectionStrings": {
       "Board": "Data Source=/absolute/path/to/data/collaboard.db"
     }
   }
   ```

   On Windows, use a Windows absolute path, escaping backslashes for JSON:

   ```jsonc
   // appsettings.json
   {
     "ConnectionStrings": {
       "Board": "Data Source=C:\\collaboard\\data\\collaboard.db"
     }
   }
   ```

   The parent directory is created automatically on first run. Your edits to
   `appsettings.json` survive upgrades — the installer performs a smart three-way
   merge that preserves operator edits, refreshes untouched defaults, and adds
   new shipped keys.

2. Run the executable:

   **macOS / Linux:**
   ```bash
   ./Collaboard.Api
   ```

   **Windows:**
   ```powershell
   .\Collaboard.Api.exe
   ```

3. Open **http://localhost:8080** in your browser.

4. Copy the **admin auth key** from the console output — you'll need it to create users and manage boards.

## Configuration

Collaboard uses `appsettings.json` for configuration. Edit it directly next to
the executable; your edits are preserved across upgrades via the smart-merge
performed by the installer.

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | `http://0.0.0.0:8080` | Bind address and port |
| `ConnectionStrings:Board` | *(required — no default)* | SQLite database path. Must be an **absolute** path; the app does not derive a path from the working or binary directory. The installer writes this into `appsettings.json`. |
| `Admin:AuthKey` | *(auto-generated)* | Override the admin auth key |

### Environment Variables

All settings can be overridden with environment variables. Nested keys use a
double-underscore (`__`) separator; there is no application-specific prefix:

```bash
export Urls=http://0.0.0.0:9090
export Admin__AuthKey=my-secret-key
export ConnectionStrings__Board="Data Source=/var/data/collaboard.db"
```

Environment variables win over `appsettings.json`.

## Database

- The SQLite database path is **required** configuration — `ConnectionStrings:Board`
  must be set to an absolute path. The installer writes this into `appsettings.json`
  (pointing at `<install-dir>/data/collaboard.db`); if you run the binary without the
  installer, set it yourself before first run or startup fails loud with the missing
  key named.
- The database file and its parent directory are created automatically on first run
- Schema migrations run automatically on startup
- The database file is backed up automatically before applying new migrations
- Backups are saved as `collaboard.db.bak-{timestamp}` in the same directory

## Updating

1. Stop the running process
2. Re-run the one-line installer (recommended) — it preserves your `appsettings.json`
   edits via smart-merge, refreshes untouched shipped defaults, and adds any new
   shipped keys. The merge writes a baseline sidecar `appsettings.shipped.json`
   next to your `appsettings.json` so the next merge knows what shipped last time.
3. Or, for a manual install: download the new release, extract it, and run
   `./Collaboard.Api --merge-appsettings <shipped-appsettings.json> ./appsettings.json --baseline ./appsettings.shipped.json`
   from the install directory.
4. Start the app — migrations run automatically.

## Version

Check the installed version:

```bash
./Collaboard.Api --version
```

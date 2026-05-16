# Collaboard — Setup Guide

## Quick Start

If you used the one-line installer (see [Installation Guide](docs/installation.md)),
`appsettings.Local.json` is already written for you — skip to step 2.

1. **Set the database path (required before first run).** Collaboard requires
   `ConnectionStrings:Board` to be an absolute path and does not derive one from
   the working or binary directory — startup fails loud if it is unset. Create
   `appsettings.Local.json` next to the executable with an absolute path:

   ```jsonc
   // appsettings.Local.json
   {
     "ConnectionStrings": {
       "Board": "Data Source=/absolute/path/to/data/collaboard.db"
     }
   }
   ```

   On Windows, use a Windows absolute path, escaping backslashes for JSON:

   ```jsonc
   // appsettings.Local.json
   {
     "ConnectionStrings": {
       "Board": "Data Source=C:\\collaboard\\data\\collaboard.db"
     }
   }
   ```

   The parent directory is created automatically on first run.

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

Collaboard uses `appsettings.json` for configuration. Create `appsettings.Local.json` next to the executable to override defaults without modifying the shipped config.

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | `http://0.0.0.0:8080` | Bind address and port |
| `ConnectionStrings:Board` | *(required — no default)* | SQLite database path. Must be an **absolute** path; the app does not derive a path from the working or binary directory. The installer writes this into `appsettings.Local.json`. |
| `Admin:AuthKey` | *(auto-generated)* | Override the admin auth key |

### Environment Variables

All settings can be overridden with environment variables. Nested keys use a
double-underscore (`__`) separator; there is no application-specific prefix:

```bash
export Urls=http://0.0.0.0:9090
export Admin__AuthKey=my-secret-key
export ConnectionStrings__Board="Data Source=/var/data/collaboard.db"
```

## Database

- The SQLite database path is **required** configuration — `ConnectionStrings:Board`
  must be set to an absolute path. The installer writes this into
  `appsettings.Local.json` (pointing at `<install-dir>/data/collaboard.db`); if you
  run the binary without the installer, set it yourself before first run or startup
  fails loud with the missing key named.
- The database file and its parent directory are created automatically on first run
- Schema migrations run automatically on startup
- The database file is backed up automatically before applying new migrations
- Backups are saved as `collaboard.db.bak-{timestamp}` in the same directory

## Updating

1. Stop the running process
2. Replace the executable (keep your `appsettings.Local.json` and `data/` directory)
3. Start the app — migrations run automatically

## Version

Check the installed version:

```bash
./Collaboard.Api --version
```

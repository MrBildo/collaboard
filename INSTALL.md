# Collaboard — Setup Guide

## Quick Start

1. Run the executable:

   **macOS / Linux:**
   ```bash
   ./Collaboard.Api
   ```

   **Windows:**
   ```powershell
   .\Collaboard.Api.exe
   ```

2. Open **http://localhost:8080** in your browser.

3. Copy the **admin auth key** from the console output — you'll need it to create users and manage boards.

## Configuration

Collaboard uses `appsettings.json` for configuration. Create `appsettings.Local.json` next to the executable to override defaults without modifying the shipped config.

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | `http://0.0.0.0:8080` | Bind address and port |
| `ConnectionStrings:Board` | `Data Source=./data/collaboard.db` | SQLite database path |
| `Admin:AuthKey` | *(auto-generated)* | Override the admin auth key |

### Environment Variables

All settings can be overridden with environment variables using double-underscore separators:

```bash
export COLLABOARD__Urls=http://0.0.0.0:9090
export COLLABOARD__Admin__AuthKey=my-secret-key
export COLLABOARD__ConnectionStrings__Board="Data Source=/var/data/collaboard.db"
```

## Database

- SQLite database is created automatically on first run at `./data/collaboard.db`
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

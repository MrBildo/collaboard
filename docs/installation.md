# Installation

## One-Line Install

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

## Manual Download

Download the latest release for your platform from [GitHub Releases](https://github.com/MrBildo/collaboard/releases/latest):

| Platform | Artifact |
|----------|----------|
| Windows 64-bit | `collattice-win-x64.zip` |
| macOS Apple Silicon | `collattice-osx-arm64.tar.gz` |
| macOS Intel | `collattice-osx-x64.tar.gz` |
| Linux 64-bit | `collattice-linux-x64.tar.gz` |
| Linux ARM64 | `collattice-linux-arm64.tar.gz` |

Extract the archive. Before the first run, edit `appsettings.json` next to the
executable to set an absolute database path — Collaboard requires
`ConnectionStrings:Board` and does not derive a path from the working or binary
directory (the one-line installers above do this for you):

```jsonc
// appsettings.json
{
  "ConnectionStrings": {
    "Board": "Data Source=/absolute/path/to/data/collaboard.db"
  }
}
```

Your edits to `appsettings.json` survive upgrades — when you re-run the one-line
installer it performs a smart three-way merge.

Then run the executable. No runtime or framework installation required.

## macOS Gatekeeper

On macOS, you may need to remove the quarantine attribute after downloading:

```bash
xattr -d com.apple.quarantine ./Collaboard.Api
```

## First Run

1. Run the executable
2. Open **http://localhost:8080** in your browser
3. Copy the admin auth key from the console output
4. Enter the key on the login screen

The admin key is printed once on first startup. To set a persistent key, see [Host Configuration](../README.md#host-configuration).

## Updating

1. Stop the running process
2. Re-run the one-line installer (recommended) — it preserves your `appsettings.json`
   edits via smart-merge, refreshes untouched shipped defaults, and adds any new
   shipped keys. The `data/` directory is left untouched.
3. For a manual install: extract the new release and run
   `./Collaboard.Api --merge-appsettings <new-appsettings.json> ./appsettings.json --baseline ./appsettings.shipped.json`
   from the install directory.
4. Start the app — migrations run automatically, database is backed up first.

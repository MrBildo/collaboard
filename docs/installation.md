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
| Windows 64-bit | `collaboard-win-x64.zip` |
| macOS Apple Silicon | `collaboard-osx-arm64.tar.gz` |
| macOS Intel | `collaboard-osx-x64.tar.gz` |
| Linux 64-bit | `collaboard-linux-x64.tar.gz` |
| Linux ARM64 | `collaboard-linux-arm64.tar.gz` |

Extract and run the executable. No runtime or framework installation required.

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
2. Download the new release for your platform
3. Replace the executable (keep your `data/` directory and `appsettings.Local.json`)
4. Start the app — migrations run automatically, database is backed up first

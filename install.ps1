$ErrorActionPreference = 'Stop'

$Repo = 'MrBildo/collaboard'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Collaboard'
$ArtifactName = 'collaboard-win-x64'

Write-Host "Install directory: $InstallDir"
Write-Host

# Get latest release tag
Write-Host 'Fetching latest release...'
$release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest"
$tag = $release.tag_name

if (-not $tag) {
    Write-Error 'Failed to fetch latest release.'
    exit 1
}

Write-Host "Latest release: $tag"

# Download artifact
$downloadUrl = "https://github.com/$Repo/releases/download/$tag/$ArtifactName.zip"
$tempFile = Join-Path ([IO.Path]::GetTempPath()) "$ArtifactName.zip"

Write-Host "Downloading $ArtifactName.zip..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempFile -UseBasicParsing

# Extract to temp location first, then merge (preserving data/ and user config)
Write-Host "Extracting to $InstallDir..."
$tempExtract = Join-Path ([IO.Path]::GetTempPath()) "collaboard-extract"
if (Test-Path $tempExtract) {
    Remove-Item $tempExtract -Recurse -Force
}

Expand-Archive -Path $tempFile -DestinationPath $tempExtract -Force

# The archive contains a subdirectory — find the actual files
$inner = Join-Path $tempExtract $ArtifactName
$sourceDir = if (Test-Path $inner) { $inner } else { $tempExtract }

# Ensure install dir exists
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

# Copy new files over existing, preserving data/ and user config
Get-ChildItem $sourceDir | ForEach-Object {
    $dest = Join-Path $InstallDir $_.Name
    # Skip data directory (contains the database)
    if ($_.Name -eq 'data') { return }
    # Skip user config overrides
    if ($_.Name -eq 'appsettings.Production.json') { return }
    if (Test-Path $dest) {
        Remove-Item $dest -Recurse -Force
    }
    Move-Item $_.FullName -Destination $dest -Force
}

Remove-Item $tempExtract -Recurse -Force

# Clean up
Remove-Item $tempFile -Force

Write-Host
Write-Host "Collaboard installed to $InstallDir"
Write-Host

# Suggest adding to PATH
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPath -notlike "*$InstallDir*") {
    Write-Host 'To add Collaboard to your PATH, run:'
    Write-Host "  [Environment]::SetEnvironmentVariable('Path', `"$InstallDir;`$env:Path`", 'User')"
    Write-Host
}

Write-Host 'To start Collaboard:'
Write-Host "  & '$InstallDir\Collaboard.Api.exe'"
Write-Host
Write-Host 'Then open http://localhost:8080 in your browser.'

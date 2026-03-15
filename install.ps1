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

# Extract
Write-Host "Extracting to $InstallDir..."
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

Expand-Archive -Path $tempFile -DestinationPath $InstallDir -Force

# The archive contains a subdirectory — move contents up if needed
$inner = Join-Path $InstallDir $ArtifactName
if (Test-Path $inner) {
    Get-ChildItem $inner | Move-Item -Destination $InstallDir -Force
    Remove-Item $inner -Force
}

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

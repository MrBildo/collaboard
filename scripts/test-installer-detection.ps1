# Verify install.ps1's fresh-vs-existing install-location detection without
# downloading or touching a real install. Exercises the real -DryRun code path
# against synthetic LOCALAPPDATA fixtures: a fresh install must resolve to the
# Collattice location, and an install already present under the earlier name must be
# detected and kept in place so a fresh install never orphans an operator's real
# database beside a new empty one.
$ErrorActionPreference = 'Stop'

$installPs1 = if ($args.Count -ge 1) { $args[0] } else { './install.ps1' }
$base = Join-Path ([IO.Path]::GetTempPath()) ('installdet-' + [Guid]::NewGuid().ToString('N'))
$fail = 0

function Field($text, $key) {
    foreach ($line in ($text -split "`n")) {
        if ($line -match "^$key`: (.*)$") { return $Matches[1].Trim() }
    }
    return ''
}

function Check($label, $actual, $expected) {
    if ($actual -eq $expected) {
        Write-Host "ok   [$label]: $actual"
    }
    else {
        Write-Host "FAIL [$label]: expected '$expected', got '$actual'"
        $script:fail = 1
    }
}

function DryRun($localAppData) {
    # Child process so install.ps1's `exit` ends only the child, not this test.
    $env:LOCALAPPDATA = $localAppData
    return (& pwsh -NoProfile -File $installPs1 -DryRun) -join "`n"
}

# Fresh -- clean LOCALAPPDATA
$h = Join-Path $base 'fresh'; New-Item -ItemType Directory -Force -Path $h | Out-Null
$out = DryRun $h
Check 'fresh/kind' (Field $out 'install-kind') 'fresh'
Check 'fresh/dir'  (Field $out 'install-dir')  (Join-Path $h 'Collattice')
Check 'fresh/db'   (Field $out 'db-path')      (Join-Path (Join-Path $h 'Collattice') (Join-Path 'data' 'collattice.db'))

# Existing -- old dir carries an appsettings.json marker
$h = Join-Path $base 'exist-appsettings'; New-Item -ItemType Directory -Force -Path (Join-Path $h 'Collaboard') | Out-Null
Set-Content -Path (Join-Path $h 'Collaboard/appsettings.json') -Value '{}'
$out = DryRun $h
Check 'exist-appsettings/kind' (Field $out 'install-kind') 'existing'
Check 'exist-appsettings/dir'  (Field $out 'install-dir')  (Join-Path $h 'Collaboard')

# Existing -- old dir carries a data/collaboard.db marker
$h = Join-Path $base 'exist-data'; New-Item -ItemType Directory -Force -Path (Join-Path $h 'Collaboard/data') | Out-Null
Set-Content -Path (Join-Path $h 'Collaboard/data/collaboard.db') -Value 'db'
$out = DryRun $h
Check 'exist-data/kind' (Field $out 'install-kind') 'existing'

# Empty old dir (no markers) -> fresh
$h = Join-Path $base 'emptyold'; New-Item -ItemType Directory -Force -Path (Join-Path $h 'Collaboard') | Out-Null
$out = DryRun $h
Check 'emptyold/kind' (Field $out 'install-kind') 'fresh'

Remove-Item -Recurse -Force $base
if ($fail -ne 0) {
    Write-Error 'install.ps1 detection: FAILURES'
}
Write-Host 'install.ps1 detection: all scenarios passed'

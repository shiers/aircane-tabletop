#!/usr/bin/env pwsh
#
# Bumps the app version from a single source of truth: the repo-root VERSION file.
#
# Usage: scripts/bump-version.ps1 <new-version>
#   e.g. scripts/bump-version.ps1 0.2.0
#
# See scripts/bump-version.sh for the full rationale. The backend csproj reads
# VERSION directly at build; this script writes VERSION and syncs the two
# package.json files and tauri.conf.json (whose "version" must be a literal
# string, since Tauri parses a file-path value as JSON).
param(
  [Parameter(Mandatory = $true, Position = 0)]
  [string]$NewVersion
)

$ErrorActionPreference = 'Stop'

if ($NewVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$') {
  Write-Error "'$NewVersion' is not a valid version (expected MAJOR.MINOR.PATCH)."
  exit 1
}

$ScriptDir = $PSScriptRoot
$RepoRoot  = Split-Path -Parent $ScriptDir

# Write VERSION with a trailing newline, no BOM.
[System.IO.File]::WriteAllText((Join-Path $RepoRoot 'VERSION'), "$NewVersion`n")
Write-Host "VERSION -> $NewVersion"

function Update-PackageJson([string]$File) {
  if (Test-Path $File) {
    $content = Get-Content -Raw -Path $File
    # Replace only the first top-level "version": "x.y.z" to avoid touching
    # nested dependency version strings further down the file.
    $regex = [regex]'("version"\s*:\s*")[^"]*(")'
    $updated = $regex.Replace($content, "`${1}$NewVersion`${2}", 1)
    Set-Content -Path $File -Value $updated -NoNewline
    Write-Host "updated $File"
  }
}

Update-PackageJson (Join-Path $RepoRoot 'src/frontend/aircane-web/package.json')
Update-PackageJson (Join-Path $RepoRoot 'desktop/package.json')
# tauri.conf.json uses the same top-level "version": "x.y.z" shape.
Update-PackageJson (Join-Path $RepoRoot 'desktop/src-tauri/tauri.conf.json')

Write-Host 'Done. Review the diff, then commit VERSION, the package.json changes, and tauri.conf.json.'

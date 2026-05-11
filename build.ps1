# ─────────────────────────────────────────────────────────────────────────────
# Aircane Tabletop — Production Build Script (PowerShell)
#
# Builds both the ASP.NET Core backend and the Vue frontend into a dist/ folder
# ready for packaging or deployment.
#
# Prerequisites:
#   - .NET SDK 8.0+
#   - Node.js 20+ and npm
#
# Usage:
#   ./build.ps1
#
# Output:
#   dist/backend/   — published ASP.NET Core app
#   dist/frontend/  — static Vue/Vite build output
# ─────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DistDir = Join-Path $ScriptDir "dist"

Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host " Aircane Tabletop — Production Build"
Write-Host "═══════════════════════════════════════════════════════════════"

# Clean previous build output
Write-Host ""
Write-Host "▸ Cleaning dist/ directory..."
if (Test-Path $DistDir) {
    Remove-Item -Recurse -Force $DistDir
}
New-Item -ItemType Directory -Path "$DistDir/backend" -Force | Out-Null
New-Item -ItemType Directory -Path "$DistDir/frontend" -Force | Out-Null

# ─────────────────────────────────────────────
# Backend build
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "▸ Building backend (Release)..."
dotnet publish "$ScriptDir/src/backend/Aircane.Api/Aircane.Api.csproj" `
    -c Release `
    -o "$DistDir/backend" `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Backend build failed" }
Write-Host "  ✓ Backend published to dist/backend/"

# ─────────────────────────────────────────────
# Frontend build
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "▸ Installing frontend dependencies..."
Push-Location "$ScriptDir/src/frontend/aircane-web"
try {
    npm ci --silent
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }

    Write-Host "▸ Building frontend (production)..."
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }

    Write-Host "▸ Copying frontend build output..."
    Copy-Item -Recurse -Force "dist/*" "$DistDir/frontend/"
    Write-Host "  ✓ Frontend built to dist/frontend/"
}
finally {
    Pop-Location
}

# ─────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════"
Write-Host " Build complete!"
Write-Host ""
Write-Host "   Backend:  $DistDir/backend/"
Write-Host "   Frontend: $DistDir/frontend/"
Write-Host ""
Write-Host " To run locally:"
Write-Host "   cd dist/backend; dotnet Aircane.Api.dll"
Write-Host "   (serve dist/frontend/ with any static file server)"
Write-Host "═══════════════════════════════════════════════════════════════"

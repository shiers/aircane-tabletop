#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Aircane Tabletop — Production Build Script
#
# Builds both the ASP.NET Core backend and the Vue frontend into a dist/ folder
# ready for packaging or deployment.
#
# Prerequisites:
#   - .NET SDK 8.0+
#   - Node.js 20+ and npm
#
# Usage:
#   ./build.sh
#
# Output:
#   dist/backend/   — published ASP.NET Core app (self-contained optional)
#   dist/frontend/  — static Vue/Vite build output
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_DIR="${SCRIPT_DIR}/dist"

echo "═══════════════════════════════════════════════════════════════"
echo " Aircane Tabletop — Production Build"
echo "═══════════════════════════════════════════════════════════════"

# Clean previous build output
echo ""
echo "▸ Cleaning dist/ directory..."
rm -rf "${DIST_DIR}"
mkdir -p "${DIST_DIR}/backend" "${DIST_DIR}/frontend"

# ─────────────────────────────────────────────
# Backend build
# ─────────────────────────────────────────────
echo ""
echo "▸ Building backend (Release)..."
dotnet publish "${SCRIPT_DIR}/src/backend/Aircane.Api/Aircane.Api.csproj" \
  -c Release \
  -o "${DIST_DIR}/backend" \
  --nologo

echo "  ✓ Backend published to dist/backend/"

# ─────────────────────────────────────────────
# Frontend build
# ─────────────────────────────────────────────
echo ""
echo "▸ Installing frontend dependencies..."
cd "${SCRIPT_DIR}/src/frontend/aircane-web"
npm ci --silent

echo "▸ Building frontend (production)..."
npm run build

echo "▸ Copying frontend build output..."
cp -r dist/* "${DIST_DIR}/frontend/"

echo "  ✓ Frontend built to dist/frontend/"

# ─────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo " Build complete!"
echo ""
echo "   Backend:  ${DIST_DIR}/backend/"
echo "   Frontend: ${DIST_DIR}/frontend/"
echo ""
echo " To run locally:"
echo "   cd dist/backend && dotnet Aircane.Api.dll"
echo "   (serve dist/frontend/ with any static file server)"
echo "═══════════════════════════════════════════════════════════════"

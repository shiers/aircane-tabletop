#!/usr/bin/env bash
#
# Builds the Aircane Tabletop desktop app (macOS / Linux).
#
# Steps:
#   1. Build the Vue frontend (static files -> src/frontend/aircane-web/dist).
#   2. Publish the ASP.NET Core backend as a self-contained single-file sidecar,
#      named with Tauri's target-triple convention.
#   3. Vendor the QR library used by the tray QR window.
#   4. Build the Tauri app (installers land in src-tauri/target/release/bundle).
#
# Run from the desktop/ directory: ./build.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FRONTEND_DIR="$REPO_ROOT/src/frontend/aircane-web"
BACKEND_PROJ="$REPO_ROOT/src/backend/Aircane.Api/Aircane.Api.csproj"
BINARIES_DIR="$SCRIPT_DIR/src-tauri/binaries"

# ── Resolve the Rust host target triple and map it to a .NET RID ───────────────
RUST_TARGET="$(rustc -vV | sed -n 's/host: //p' | tr -d '[:space:]')"

case "$RUST_TARGET" in
  x86_64-apple-darwin)        DOTNET_RID="osx-x64" ;;
  aarch64-apple-darwin)       DOTNET_RID="osx-arm64" ;;
  x86_64-unknown-linux-gnu)   DOTNET_RID="linux-x64" ;;
  aarch64-unknown-linux-gnu)  DOTNET_RID="linux-arm64" ;;
  *)
    echo "ERROR: Unsupported Rust target triple '$RUST_TARGET'. Add a mapping in build.sh." >&2
    exit 1
    ;;
esac

echo "Rust target : $RUST_TARGET"
echo ".NET RID    : $DOTNET_RID"

# ── 1. Build the Vue frontend ──────────────────────────────────────────────────
echo "==> Building frontend"
cd "$FRONTEND_DIR"
npm ci
npm run build

# ── 2. Publish the ASP.NET Core sidecar ────────────────────────────────────────
echo "==> Publishing backend sidecar"
mkdir -p "$BINARIES_DIR"
dotnet publish "$BACKEND_PROJ" \
  -c Release \
  -r "$DOTNET_RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o "$BINARIES_DIR"

# Rename the published executable to the Tauri sidecar convention:
#   aircane-server-<target-triple>
SRC_BIN="$BINARIES_DIR/Aircane.Api"
DEST_BIN="$BINARIES_DIR/aircane-server-$RUST_TARGET"
if [ ! -f "$SRC_BIN" ]; then
  echo "ERROR: expected published binary at '$SRC_BIN' not found." >&2
  exit 1
fi
mv -f "$SRC_BIN" "$DEST_BIN"
chmod +x "$DEST_BIN"
echo "Sidecar     : $DEST_BIN"

# ── 3. Vendor the QR library used by the tray QR window (offline-safe) ────────
# The wrapper's shell pages (loading/error/qr .html) live in the frontend's
# public/desktop/ folder, so Vite already copied them into dist/desktop during
# step 1. The QR window needs the qrcode library next to qr.html; copy it into
# both public/ (so `cargo tauri dev` and future builds have it) and this build's
# dist output.
echo "==> Vendoring qrcode.min.js"
QR_SRC="$FRONTEND_DIR/node_modules/qrcode/build/qrcode.min.js"
PUBLIC_DESKTOP="$FRONTEND_DIR/public/desktop"
DIST_DESKTOP="$FRONTEND_DIR/dist/desktop"
if [ -f "$QR_SRC" ]; then
  mkdir -p "$PUBLIC_DESKTOP" "$DIST_DESKTOP"
  cp -f "$QR_SRC" "$PUBLIC_DESKTOP/qrcode.min.js"
  cp -f "$QR_SRC" "$DIST_DESKTOP/qrcode.min.js"
else
  echo "WARNING: $QR_SRC not found; the tray QR window will not render. Run 'npm ci' in the frontend first." >&2
fi

# ── 4. Build the Tauri app ─────────────────────────────────────────────────────
echo "==> Building Tauri app"
cd "$SCRIPT_DIR"
if command -v cargo >/dev/null 2>&1 && cargo tauri --version >/dev/null 2>&1; then
  cargo tauri build
else
  # Fall back to the npm-installed CLI.
  npm ci
  npx tauri build
fi

echo "Done. Installers are in $SCRIPT_DIR/src-tauri/target/release/bundle/"

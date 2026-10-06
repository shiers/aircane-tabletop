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

# ── 2b. Fetch the cloudflared sidecar (pinned + checksum-verified) ─────────────
# cloudflared powers internet play (Cloudflare Tunnel). It is fetched at build time
# from the official Cloudflare release and verified against the pinned SHA256 in
# binaries/cloudflared-versions.json. The build FAILS if the checksum does not match,
# so an unverified binary is never bundled. See docs/setup/desktop.md.
echo "==> Fetching cloudflared sidecar"
CF_MANIFEST="$BINARIES_DIR/cloudflared-versions.json"
if [ ! -f "$CF_MANIFEST" ]; then
  echo "ERROR: cloudflared manifest not found at '$CF_MANIFEST'." >&2
  exit 1
fi

# Minimal JSON extraction via python3 (available on macOS/Linux CI) to avoid a jq dependency.
read_manifest() {
  python3 - "$CF_MANIFEST" "$RUST_TARGET" "$1" <<'PY'
import json, sys
manifest_path, target, key = sys.argv[1], sys.argv[2], sys.argv[3]
with open(manifest_path) as f:
    m = json.load(f)
if key == "version":
    print(m["version"]); sys.exit(0)
if key == "baseUrl":
    print(m["baseUrl"]); sys.exit(0)
entry = m["assets"].get(target)
if not entry:
    sys.exit(3)
print(entry.get(key, ""))
PY
}

CF_VERSION="$(read_manifest version)"
CF_BASEURL="$(read_manifest baseUrl)"
CF_ASSET="$(read_manifest asset)" || { echo "ERROR: no cloudflared asset pinned for target '$RUST_TARGET'." >&2; exit 1; }
if [ -z "$CF_ASSET" ]; then
  echo "ERROR: no cloudflared asset pinned for target '$RUST_TARGET' in cloudflared-versions.json." >&2
  exit 1
fi
CF_ARCHIVE="$(read_manifest archive)"
CF_SHA256="$(read_manifest sha256)"
CF_MEMBER="$(read_manifest member)"

CF_DEST="$BINARIES_DIR/cloudflared-$RUST_TARGET"
CF_URL="$CF_BASEURL/$CF_VERSION/$CF_ASSET"
echo "cloudflared : $CF_VERSION ($CF_ASSET)"

if [ -f "$CF_DEST" ]; then
  echo "cloudflared already present; skipping download."
else
  CF_DOWNLOAD="$BINARIES_DIR/$CF_ASSET"
  curl -fsSL -o "$CF_DOWNLOAD" "$CF_URL"

  # Verify the checksum of the downloaded asset BEFORE trusting/extracting it.
  if command -v sha256sum >/dev/null 2>&1; then
    ACTUAL_HASH="$(sha256sum "$CF_DOWNLOAD" | awk '{print $1}')"
  else
    ACTUAL_HASH="$(shasum -a 256 "$CF_DOWNLOAD" | awk '{print $1}')"
  fi
  EXPECTED_HASH="$(printf '%s' "$CF_SHA256" | tr '[:upper:]' '[:lower:]')"
  ACTUAL_HASH="$(printf '%s' "$ACTUAL_HASH" | tr '[:upper:]' '[:lower:]')"
  if [ "$ACTUAL_HASH" != "$EXPECTED_HASH" ]; then
    rm -f "$CF_DOWNLOAD"
    echo "ERROR: cloudflared checksum mismatch for '$CF_ASSET'." >&2
    echo "  expected: $EXPECTED_HASH" >&2
    echo "  actual:   $ACTUAL_HASH" >&2
    echo "Refusing to bundle an unverified binary." >&2
    exit 1
  fi
  echo "cloudflared checksum OK ($EXPECTED_HASH)."

  case "$CF_ARCHIVE" in
    none)
      mv -f "$CF_DOWNLOAD" "$CF_DEST"
      ;;
    tgz)
      CF_EXTRACT_DIR="$BINARIES_DIR/cf-extract"
      mkdir -p "$CF_EXTRACT_DIR"
      tar -xzf "$CF_DOWNLOAD" -C "$CF_EXTRACT_DIR"
      if [ ! -f "$CF_EXTRACT_DIR/$CF_MEMBER" ]; then
        echo "ERROR: expected '$CF_MEMBER' inside '$CF_ASSET' but it was not found." >&2
        exit 1
      fi
      mv -f "$CF_EXTRACT_DIR/$CF_MEMBER" "$CF_DEST"
      rm -rf "$CF_EXTRACT_DIR"
      rm -f "$CF_DOWNLOAD"
      ;;
    *)
      echo "ERROR: unknown archive type '$CF_ARCHIVE' in cloudflared-versions.json." >&2
      exit 1
      ;;
  esac
  chmod +x "$CF_DEST"
fi
echo "cloudflared : $CF_DEST"

# ── 2c. Fetch OCR assets (pinned + checksum-verified) ──────────────────────────
# OCR is a first-class capability bundled with the desktop app. The English
# Tesseract model (eng.traineddata) is required on every target; the optional
# native Tesseract/Leptonica libs are fetched per target when pinned (Windows
# gets its native libtesseract from the TesseractOCR NuGet self-contained publish,
# so it has no nativeLib entry). Every asset is fetched from the URL composed by
# the SAME three-segment rule as cloudflared — <group>.baseUrl/<group>.version/<asset>
# — and verified against the pinned SHA256 in binaries/ocr-assets-versions.json.
# The build FAILS on a checksum mismatch, so an unverified asset is never bundled.
# See docs/setup/ocr.md.
echo "==> Fetching OCR assets"
OCR_MANIFEST="$BINARIES_DIR/ocr-assets-versions.json"
if [ ! -f "$OCR_MANIFEST" ]; then
  echo "ERROR: OCR assets manifest not found at '$OCR_MANIFEST'." >&2
  exit 1
fi

# Minimal JSON extraction via python3, mirroring read_manifest. Composes the same
# three-segment baseUrl/version/asset URL rule as cloudflared, per group. Usage:
#   read_ocr <group> version|baseUrl             -> group-level field
#   read_ocr <group> <target> asset|archive|sha256|members  -> per-target asset field
# A missing per-target entry exits 3 (caller treats nativeLib as optional).
read_ocr() {
  python3 - "$OCR_MANIFEST" "$@" <<'PY'
import json, sys
manifest_path = sys.argv[1]
group = sys.argv[2]
with open(manifest_path) as f:
    m = json.load(f)
g = m[group]
if len(sys.argv) == 4 and sys.argv[3] in ("version", "baseUrl"):
    print(g[sys.argv[3]]); sys.exit(0)
# per-target: argv[3]=target, argv[4]=key
target, key = sys.argv[3], sys.argv[4]
entry = g["assets"].get(target)
if not entry:
    sys.exit(3)
val = entry.get(key, "")
if isinstance(val, list):
    print("\n".join(val))
else:
    print(val)
PY
}

# -- tessdata (required on every target): eng.traineddata --
TESS_VERSION="$(read_ocr tessdata version)"
TESS_BASEURL="$(read_ocr tessdata baseUrl)"
TESS_ASSET="$(read_ocr tessdata all asset)"
TESS_SHA256="$(read_ocr tessdata all sha256)"
TESSDATA_DIR="$BINARIES_DIR/tessdata"
TESSDATA_DEST="$TESSDATA_DIR/eng.traineddata"
TESS_URL="$TESS_BASEURL/$TESS_VERSION/$TESS_ASSET"
echo "tessdata    : $TESS_VERSION ($TESS_ASSET)"

if [ -f "$TESSDATA_DEST" ]; then
  echo "tessdata already present; skipping download."
else
  mkdir -p "$TESSDATA_DIR"
  TESS_DOWNLOAD="$BINARIES_DIR/$TESS_ASSET"
  curl -fsSL -o "$TESS_DOWNLOAD" "$TESS_URL"

  # Verify the checksum of the downloaded asset BEFORE trusting/bundling it.
  if command -v sha256sum >/dev/null 2>&1; then
    ACTUAL_HASH="$(sha256sum "$TESS_DOWNLOAD" | awk '{print $1}')"
  else
    ACTUAL_HASH="$(shasum -a 256 "$TESS_DOWNLOAD" | awk '{print $1}')"
  fi
  EXPECTED_HASH="$(printf '%s' "$TESS_SHA256" | tr '[:upper:]' '[:lower:]')"
  ACTUAL_HASH="$(printf '%s' "$ACTUAL_HASH" | tr '[:upper:]' '[:lower:]')"
  if [ "$ACTUAL_HASH" != "$EXPECTED_HASH" ]; then
    rm -f "$TESS_DOWNLOAD"
    echo "ERROR: tessdata checksum mismatch for '$TESS_ASSET'." >&2
    echo "  expected: $EXPECTED_HASH" >&2
    echo "  actual:   $ACTUAL_HASH" >&2
    echo "Refusing to bundle an unverified asset." >&2
    exit 1
  fi
  echo "tessdata checksum OK ($EXPECTED_HASH)."
  mv -f "$TESS_DOWNLOAD" "$TESSDATA_DEST"
fi
echo "tessdata    : $TESSDATA_DEST"

# -- nativeLib (optional per target): native Tesseract/Leptonica libs --
OCR_NATIVE_DIR="$BINARIES_DIR/ocr-native"
# tauri.conf.json maps the `binaries/ocr-native/*` resource glob, which fails the
# build when it matches nothing. Keep a marker file so the dir is never empty.
mkdir -p "$OCR_NATIVE_DIR"
echo "Bundled native OCR libs (empty when the target relies on NuGet/system libs)." > "$OCR_NATIVE_DIR/README.txt"
# An all-zero sha256 marks a documented placeholder entry, not a real pinned
# asset; treat it as "no pinned lib" instead of downloading from example.invalid.
NATIVE_PLACEHOLDER_SHA="0000000000000000000000000000000000000000000000000000000000000000"
if NATIVE_ASSET="$(read_ocr nativeLib "$RUST_TARGET" asset)" \
   && [ "$(read_ocr nativeLib "$RUST_TARGET" sha256)" = "$NATIVE_PLACEHOLDER_SHA" ]; then
  echo "Native OCR lib for '$RUST_TARGET' is a placeholder in ocr-assets-versions.json; relying on system libs."
elif NATIVE_ASSET="$(read_ocr nativeLib "$RUST_TARGET" asset)"; then
  NATIVE_VERSION="$(read_ocr nativeLib version)"
  NATIVE_BASEURL="$(read_ocr nativeLib baseUrl)"
  NATIVE_ARCHIVE="$(read_ocr nativeLib "$RUST_TARGET" archive)"
  NATIVE_SHA256="$(read_ocr nativeLib "$RUST_TARGET" sha256)"
  NATIVE_URL="$NATIVE_BASEURL/$NATIVE_VERSION/$NATIVE_ASSET"
  echo "ocr-native  : $NATIVE_VERSION ($NATIVE_ASSET)"
  mkdir -p "$OCR_NATIVE_DIR"
  NATIVE_DOWNLOAD="$BINARIES_DIR/$NATIVE_ASSET"
  curl -fsSL -o "$NATIVE_DOWNLOAD" "$NATIVE_URL"

  # Verify the checksum of the downloaded archive BEFORE extracting it.
  if command -v sha256sum >/dev/null 2>&1; then
    ACTUAL_HASH="$(sha256sum "$NATIVE_DOWNLOAD" | awk '{print $1}')"
  else
    ACTUAL_HASH="$(shasum -a 256 "$NATIVE_DOWNLOAD" | awk '{print $1}')"
  fi
  EXPECTED_HASH="$(printf '%s' "$NATIVE_SHA256" | tr '[:upper:]' '[:lower:]')"
  ACTUAL_HASH="$(printf '%s' "$ACTUAL_HASH" | tr '[:upper:]' '[:lower:]')"
  if [ "$ACTUAL_HASH" != "$EXPECTED_HASH" ]; then
    rm -f "$NATIVE_DOWNLOAD"
    echo "ERROR: native OCR lib checksum mismatch for '$NATIVE_ASSET'." >&2
    echo "  expected: $EXPECTED_HASH" >&2
    echo "  actual:   $ACTUAL_HASH" >&2
    echo "Refusing to bundle an unverified asset." >&2
    exit 1
  fi
  echo "ocr-native checksum OK ($EXPECTED_HASH)."

  case "$NATIVE_ARCHIVE" in
    tgz)
      OCR_EXTRACT_DIR="$BINARIES_DIR/ocr-extract"
      mkdir -p "$OCR_EXTRACT_DIR"
      tar -xzf "$NATIVE_DOWNLOAD" -C "$OCR_EXTRACT_DIR"
      while IFS= read -r member; do
        [ -z "$member" ] && continue
        if [ ! -f "$OCR_EXTRACT_DIR/$member" ]; then
          echo "ERROR: expected '$member' inside '$NATIVE_ASSET' but it was not found." >&2
          exit 1
        fi
        mv -f "$OCR_EXTRACT_DIR/$member" "$OCR_NATIVE_DIR/$(basename "$member")"
      done < <(read_ocr nativeLib "$RUST_TARGET" members)
      rm -rf "$OCR_EXTRACT_DIR"
      rm -f "$NATIVE_DOWNLOAD"
      ;;
    *)
      echo "ERROR: unknown archive type '$NATIVE_ARCHIVE' for native OCR lib in ocr-assets-versions.json." >&2
      exit 1
      ;;
  esac
  echo "ocr-native  : $OCR_NATIVE_DIR"
else
  rc=$?
  if [ "$rc" -eq 3 ]; then
    echo "No pinned native OCR lib for '$RUST_TARGET'; relying on NuGet/system libs."
  else
    exit "$rc"
  fi
fi

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

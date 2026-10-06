#!/usr/bin/env pwsh
#
# Builds the Aircane Tabletop desktop app (Windows).
#
# Steps:
#   1. Build the Vue frontend (static files -> src/frontend/aircane-web/dist).
#   2. Publish the ASP.NET Core backend as a self-contained single-file sidecar,
#      named with Tauri's target-triple convention (.exe on Windows).
#   3. Vendor the QR library used by the tray QR window.
#   4. Build the Tauri app (installers land in src-tauri/target/release/bundle).
#
# Run from the desktop/ directory: ./build.ps1
$ErrorActionPreference = 'Stop'

$ScriptDir   = $PSScriptRoot
$RepoRoot    = Split-Path -Parent $ScriptDir
$FrontendDir = Join-Path $RepoRoot 'src/frontend/aircane-web'
$BackendProj = Join-Path $RepoRoot 'src/backend/Aircane.Api/Aircane.Api.csproj'
$BinariesDir = Join-Path $ScriptDir 'src-tauri/binaries'

# ── Resolve the Rust host target triple and map it to a .NET RID ───────────────
$RustTarget = (& rustc -vV | Select-String -Pattern '^host:\s*(.+)$').Matches.Groups[1].Value.Trim()

switch ($RustTarget) {
  'x86_64-pc-windows-msvc'  { $DotnetRid = 'win-x64' }
  'aarch64-pc-windows-msvc' { $DotnetRid = 'win-arm64' }
  default {
    Write-Error "Unsupported Rust target triple '$RustTarget'. Add a mapping in build.ps1."
    exit 1
  }
}

Write-Host "Rust target : $RustTarget"
Write-Host ".NET RID    : $DotnetRid"

# ── 1. Build the Vue frontend ──────────────────────────────────────────────────
Write-Host '==> Building frontend'
Push-Location $FrontendDir
try {
  npm ci
  npm run build
}
finally { Pop-Location }

# ── 2. Publish the ASP.NET Core sidecar ────────────────────────────────────────
Write-Host '==> Publishing backend sidecar'
New-Item -ItemType Directory -Force -Path $BinariesDir | Out-Null
dotnet publish $BackendProj `
  -c Release `
  -r $DotnetRid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o $BinariesDir

# Rename the published executable to the Tauri sidecar convention:
#   aircane-server-<target-triple>.exe
$SrcBin  = Join-Path $BinariesDir 'Aircane.Api.exe'
$DestBin = Join-Path $BinariesDir "aircane-server-$RustTarget.exe"
if (-not (Test-Path $SrcBin)) {
  Write-Error "Expected published binary at '$SrcBin' not found."
  exit 1
}
Move-Item -Force -Path $SrcBin -Destination $DestBin
Write-Host "Sidecar     : $DestBin"

# ── 2b. Fetch the cloudflared sidecar (pinned + checksum-verified) ─────────────
# cloudflared powers internet play (Cloudflare Tunnel). It is fetched at build time
# from the official Cloudflare release and verified against the pinned SHA256 in
# binaries/cloudflared-versions.json. The build FAILS if the checksum does not match,
# so an unverified binary is never bundled. See docs/setup/desktop.md.
Write-Host '==> Fetching cloudflared sidecar'
$CfManifestPath = Join-Path $BinariesDir 'cloudflared-versions.json'
if (-not (Test-Path $CfManifestPath)) {
  Write-Error "cloudflared manifest not found at '$CfManifestPath'."
  exit 1
}
$CfManifest = Get-Content -Raw -Path $CfManifestPath | ConvertFrom-Json
$CfEntry = $CfManifest.assets.$RustTarget
if ($null -eq $CfEntry) {
  Write-Error "No cloudflared asset pinned for target '$RustTarget' in cloudflared-versions.json."
  exit 1
}

$CfDest = Join-Path $BinariesDir "cloudflared-$RustTarget.exe"
$CfUrl  = "$($CfManifest.baseUrl)/$($CfManifest.version)/$($CfEntry.asset)"
Write-Host "cloudflared : $($CfManifest.version) ($($CfEntry.asset))"

if (Test-Path $CfDest) {
  Write-Host "cloudflared already present; skipping download."
}
else {
  $CfDownload = Join-Path $BinariesDir $CfEntry.asset
  Invoke-WebRequest -Uri $CfUrl -OutFile $CfDownload -UseBasicParsing

  # Verify the checksum of the downloaded asset BEFORE trusting/extracting it.
  $ActualHash = (Get-FileHash -Algorithm SHA256 -Path $CfDownload).Hash.ToLowerInvariant()
  $ExpectedHash = $CfEntry.sha256.ToLowerInvariant()
  if ($ActualHash -ne $ExpectedHash) {
    Remove-Item -Force -Path $CfDownload
    Write-Error "cloudflared checksum mismatch for '$($CfEntry.asset)'.`n  expected: $ExpectedHash`n  actual:   $ActualHash`nRefusing to bundle an unverified binary."
    exit 1
  }
  Write-Host "cloudflared checksum OK ($ExpectedHash)."

  if ($CfEntry.archive -eq 'none') {
    Move-Item -Force -Path $CfDownload -Destination $CfDest
  }
  elseif ($CfEntry.archive -eq 'tgz') {
    # macOS ships cloudflared as a .tgz; extract the inner binary.
    $CfExtractDir = Join-Path $BinariesDir 'cf-extract'
    New-Item -ItemType Directory -Force -Path $CfExtractDir | Out-Null
    tar -xzf $CfDownload -C $CfExtractDir
    $Member = Join-Path $CfExtractDir $CfEntry.member
    if (-not (Test-Path $Member)) {
      Write-Error "Expected '$($CfEntry.member)' inside '$($CfEntry.asset)' but it was not found."
      exit 1
    }
    Move-Item -Force -Path $Member -Destination $CfDest
    Remove-Item -Recurse -Force -Path $CfExtractDir
    Remove-Item -Force -Path $CfDownload
  }
  else {
    Write-Error "Unknown archive type '$($CfEntry.archive)' in cloudflared-versions.json."
    exit 1
  }
}
Write-Host "cloudflared : $CfDest"

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
Write-Host '==> Fetching OCR assets'
$OcrManifestPath = Join-Path $BinariesDir 'ocr-assets-versions.json'
if (-not (Test-Path $OcrManifestPath)) {
  Write-Error "OCR assets manifest not found at '$OcrManifestPath'."
  exit 1
}
$OcrManifest = Get-Content -Raw -Path $OcrManifestPath | ConvertFrom-Json

# -- tessdata (required on every target): eng.traineddata --
$TessdataDir = Join-Path $BinariesDir 'tessdata'
$TessdataDest = Join-Path $TessdataDir 'eng.traineddata'
$TessAsset = $OcrManifest.tessdata.assets.all.asset
$TessUrl = "$($OcrManifest.tessdata.baseUrl)/$($OcrManifest.tessdata.version)/$($TessAsset)"
Write-Host "tessdata    : $($OcrManifest.tessdata.version) ($TessAsset)"

if (Test-Path $TessdataDest) {
  Write-Host "tessdata already present; skipping download."
}
else {
  New-Item -ItemType Directory -Force -Path $TessdataDir | Out-Null
  $TessDownload = Join-Path $BinariesDir $TessAsset
  Invoke-WebRequest -Uri $TessUrl -OutFile $TessDownload -UseBasicParsing

  # Verify the checksum of the downloaded asset BEFORE trusting/bundling it.
  $ActualHash = (Get-FileHash -Algorithm SHA256 -Path $TessDownload).Hash.ToLowerInvariant()
  $ExpectedHash = $OcrManifest.tessdata.assets.all.sha256.ToLowerInvariant()
  if ($ActualHash -ne $ExpectedHash) {
    Remove-Item -Force -Path $TessDownload
    Write-Error "tessdata checksum mismatch for '$TessAsset'.`n  expected: $ExpectedHash`n  actual:   $ActualHash`nRefusing to bundle an unverified asset."
    exit 1
  }
  Write-Host "tessdata checksum OK ($ExpectedHash)."
  Move-Item -Force -Path $TessDownload -Destination $TessdataDest
}
Write-Host "tessdata    : $TessdataDest"

# -- nativeLib (optional per target): native Tesseract/Leptonica libs --
$OcrNativeDir = Join-Path $BinariesDir 'ocr-native'
# tauri.conf.json maps the `binaries/ocr-native/*` resource glob, which fails the
# build when it matches nothing. Keep a marker file so the dir is never empty.
New-Item -ItemType Directory -Force -Path $OcrNativeDir | Out-Null
Set-Content -Path (Join-Path $OcrNativeDir 'README.txt') -Value 'Bundled native OCR libs (empty when the target relies on NuGet/system libs).'
$NativeEntry = $OcrManifest.nativeLib.assets.$RustTarget
if ($null -eq $NativeEntry) {
  Write-Host "No pinned native OCR lib for '$RustTarget'; relying on NuGet/system libs."
}
elseif ($NativeEntry.sha256 -match '^0{64}$') {
  # An all-zero sha256 marks a documented placeholder entry, not a real pinned asset.
  Write-Host "Native OCR lib for '$RustTarget' is a placeholder in ocr-assets-versions.json; relying on system libs."
}
else {
  New-Item -ItemType Directory -Force -Path $OcrNativeDir | Out-Null
  $NativeUrl = "$($OcrManifest.nativeLib.baseUrl)/$($OcrManifest.nativeLib.version)/$($NativeEntry.asset)"
  Write-Host "ocr-native  : $($OcrManifest.nativeLib.version) ($($NativeEntry.asset))"
  $NativeDownload = Join-Path $BinariesDir $NativeEntry.asset
  Invoke-WebRequest -Uri $NativeUrl -OutFile $NativeDownload -UseBasicParsing

  # Verify the checksum of the downloaded archive BEFORE extracting it.
  $ActualHash = (Get-FileHash -Algorithm SHA256 -Path $NativeDownload).Hash.ToLowerInvariant()
  $ExpectedHash = $NativeEntry.sha256.ToLowerInvariant()
  if ($ActualHash -ne $ExpectedHash) {
    Remove-Item -Force -Path $NativeDownload
    Write-Error "native OCR lib checksum mismatch for '$($NativeEntry.asset)'.`n  expected: $ExpectedHash`n  actual:   $ActualHash`nRefusing to bundle an unverified asset."
    exit 1
  }
  Write-Host "ocr-native checksum OK ($ExpectedHash)."

  if ($NativeEntry.archive -eq 'tgz') {
    $OcrExtractDir = Join-Path $BinariesDir 'ocr-extract'
    New-Item -ItemType Directory -Force -Path $OcrExtractDir | Out-Null
    tar -xzf $NativeDownload -C $OcrExtractDir
    foreach ($member in $NativeEntry.members) {
      $MemberPath = Join-Path $OcrExtractDir $member
      if (-not (Test-Path $MemberPath)) {
        Write-Error "Expected '$member' inside '$($NativeEntry.asset)' but it was not found."
        exit 1
      }
      Move-Item -Force -Path $MemberPath -Destination (Join-Path $OcrNativeDir (Split-Path -Leaf $member))
    }
    Remove-Item -Recurse -Force -Path $OcrExtractDir
    Remove-Item -Force -Path $NativeDownload
  }
  else {
    Write-Error "Unknown archive type '$($NativeEntry.archive)' for native OCR lib in ocr-assets-versions.json."
    exit 1
  }
  Write-Host "ocr-native  : $OcrNativeDir"
}

# ── 3. Vendor the QR library used by the tray QR window (offline-safe) ────────
# The wrapper's shell pages (loading/error/qr .html) live in the frontend's
# public/desktop/ folder, so Vite already copied them into dist/desktop during
# step 1. The QR window needs the qrcode library next to qr.html; copy it into
# both public/ (so `cargo tauri dev` and future builds have it) and this build's
# dist output.
Write-Host '==> Vendoring qrcode.min.js'
$QrSrc = Join-Path $FrontendDir 'node_modules/qrcode/build/qrcode.min.js'
$PublicDesktop = Join-Path $FrontendDir 'public/desktop'
$DistDesktop = Join-Path $FrontendDir 'dist/desktop'
if (Test-Path $QrSrc) {
  New-Item -ItemType Directory -Force -Path $PublicDesktop | Out-Null
  New-Item -ItemType Directory -Force -Path $DistDesktop | Out-Null
  Copy-Item -Force -Path $QrSrc -Destination (Join-Path $PublicDesktop 'qrcode.min.js')
  Copy-Item -Force -Path $QrSrc -Destination (Join-Path $DistDesktop 'qrcode.min.js')
}
else {
  Write-Warning "$QrSrc not found; the tray QR window will not render. Run 'npm ci' in the frontend first."
}

# ── 4. Build the Tauri app ─────────────────────────────────────────────────────
Write-Host '==> Building Tauri app'
Push-Location $ScriptDir
try {
  $hasCargoTauri = $false
  if (Get-Command cargo -ErrorAction SilentlyContinue) {
    & cargo tauri --version *> $null
    if ($LASTEXITCODE -eq 0) { $hasCargoTauri = $true }
  }
  if ($hasCargoTauri) {
    cargo tauri build
  }
  else {
    npm ci
    if ($LASTEXITCODE -ne 0) { Write-Error 'npm ci failed.'; exit $LASTEXITCODE }
    npx tauri build
  }
  # Native commands don't trip $ErrorActionPreference; fail loudly instead of printing "Done".
  if ($LASTEXITCODE -ne 0) { Write-Error 'Tauri build failed.'; exit $LASTEXITCODE }
}
finally { Pop-Location }

Write-Host "Done. Installers are in $ScriptDir/src-tauri/target/release/bundle/"

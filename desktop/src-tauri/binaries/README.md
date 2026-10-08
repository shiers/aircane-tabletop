# Sidecar binaries (generated — do not commit)

This directory holds the two **sidecar** binaries Tauri runs:

1. `aircane-server-*` — the self-contained, single-file build of the ASP.NET Core
   backend (`Aircane.Api`), produced by the build scripts via `dotnet publish`.
2. `cloudflared-*` — the Cloudflare Tunnel client for internet play, **fetched at
   build time** from the official Cloudflare release and verified against the
   pinned SHA256 in `cloudflared-versions.json`.

It also holds the **OCR assets** fetched at build time (not executables, so they
are bundled as Tauri `resources`, not `externalBin`):

3. `tessdata/eng.traineddata` — the English Tesseract model, **fetched at build
   time** from the pinned `tessdata_fast` tag and verified against the SHA256 in
   `ocr-assets-versions.json`. Required on every target.
4. `ocr-native/*` — the optional native Tesseract/Leptonica libraries for
   zero-setup OCR on Linux/macOS, **fetched + checksum-verified** the same way
   when a `nativeLib` entry is pinned for the target. Windows takes its native
   libtesseract from the `TesseractOCR` NuGet self-contained publish next to the
   sidecar, so it needs no `nativeLib` entry.

All of the above are intentionally **gitignored**. Only this README and the
pinned `cloudflared-versions.json` + `ocr-assets-versions.json` manifests are
tracked.

## Naming convention

Tauri's `externalBin` entry in `tauri.conf.json` is `binaries/aircane-server`.
At build/run time Tauri appends the **current target triple**, so the published
binary must be renamed to match:

| Rust target triple             | .NET RID     | Sidecar file name                              |
| ------------------------------ | ------------ | ---------------------------------------------- |
| `x86_64-pc-windows-msvc`       | `win-x64`    | `aircane-server-x86_64-pc-windows-msvc.exe`    |
| `aarch64-pc-windows-msvc`      | `win-arm64`  | `aircane-server-aarch64-pc-windows-msvc.exe`   |
| `x86_64-apple-darwin`          | `osx-x64`    | `aircane-server-x86_64-apple-darwin`           |
| `aarch64-apple-darwin`         | `osx-arm64`  | `aircane-server-aarch64-apple-darwin`          |
| `x86_64-unknown-linux-gnu`     | `linux-x64`  | `aircane-server-x86_64-unknown-linux-gnu`      |

The `cloudflared` sidecar follows the same convention (`cloudflared-<target-triple>[.exe]`)
and is registered alongside the backend in `tauri.conf.json`
(`externalBin: ["binaries/aircane-server", "binaries/cloudflared"]`).

The OCR assets are **not** sidecars; they are declared under `bundle.resources`
in `tauri.conf.json` (`binaries/tessdata/*` → `tessdata/`, `binaries/ocr-native/*`
→ `ocr-native/`) and located at runtime via `resource_dir()`. The build scripts
fetch each asset by the **same three-segment rule** as cloudflared —
`<group>.baseUrl/<group>.version/<asset>` — and **fail the build on a SHA256
mismatch** so an unverified asset is never bundled.

See `docs/setup/desktop.md` for the full build instructions and the pinned
cloudflared version, and `docs/setup/ocr.md` for the OCR assets and how to update
`ocr-assets-versions.json`.

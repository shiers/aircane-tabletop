# Sidecar binaries (generated — do not commit)

This directory holds the two **sidecar** binaries Tauri runs:

1. `aircane-server-*` — the self-contained, single-file build of the ASP.NET Core
   backend (`Aircane.Api`), produced by the build scripts via `dotnet publish`.
2. `cloudflared-*` — the Cloudflare Tunnel client for internet play, **fetched at
   build time** from the official Cloudflare release and verified against the
   pinned SHA256 in `cloudflared-versions.json`.

Both are intentionally **gitignored**. Only this README and the pinned
`cloudflared-versions.json` manifest are tracked.

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

See `docs/setup/desktop.md` for the full build instructions, the pinned
cloudflared version, and how to update it.

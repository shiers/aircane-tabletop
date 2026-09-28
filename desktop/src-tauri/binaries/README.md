# Sidecar binaries (generated — do not commit)

This directory holds the self-contained, single-file build of the ASP.NET Core
backend (`Aircane.Api`) that Tauri runs as a **sidecar**. The files here are
produced by the build scripts (`desktop/build.ps1` / `desktop/build.sh`) via
`dotnet publish`, and are intentionally **gitignored** — only this README is tracked.

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

See `docs/setup/desktop.md` for the full build instructions.

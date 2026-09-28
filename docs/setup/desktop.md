# Desktop Wrapper (Tauri)

Aircane Tabletop ships an optional **desktop wrapper** built with
[Tauri v2](https://v2.tauri.app/). It produces a double-click installable app for
Windows, macOS, and Linux that:

1. Starts the ASP.NET Core backend as a managed **sidecar** process.
2. Opens the Vue frontend in a native OS webview window.
3. Shows the LAN join URL (and a QR code) in the window title and system tray.
4. Detects whether Ollama is running and prompts if it isn't (only when Ollama is
   the configured AI provider).
5. Shuts the backend down cleanly when the window closes.

Players on the same LAN keep using their **browser** as normal — only the host
needs the desktop app. The wrapper is purely additive: the frontend and backend
are unchanged and continue to work when run directly (see
[getting-started.md](./getting-started.md) and [docker.md](./docker.md)).

> The desktop project lives in [`desktop/`](../../desktop/). The Vue frontend is
> **not** duplicated — in development Tauri points at the Vite dev server, and in
> production it serves the built static files from
> `src/frontend/aircane-web/dist`.

> **Requires PostgreSQL (pgvector).** The bundled backend needs a reachable
> PostgreSQL server with the `pgvector` extension — the desktop installer does
> **not** bundle a database. The sidecar applies EF Core migrations and seeds
> built-in content automatically on first run (it sets `Aircane:TrustLocalHost=true`,
> which enables local-host trust *and* first-run migration), but a Postgres
> instance must already be running and reachable via the backend's connection
> string. For now, run the `postgres` service from
> [docker-compose.yml](../../docker-compose.yml) (`docker compose up postgres -d`)
> before launching the desktop app. Bundling or embedding a database for a
> zero-dependency installer is tracked as future work — see
> [known-limitations.md](../known-limitations.md).

> **Local host is trusted.** The sidecar runs the backend in Production but sets
> `Aircane:TrustLocalHost=true`, which makes the host-only authorization policies
> permissive — the single user running the app on their own machine *is* the
> host, so they can open AI settings and create sessions without first joining
> one. LAN players still authenticate with an invite code as normal. A shared or
> hosted deployment must **never** set this flag.

---

## Prerequisites for the build environment

You only need these to **build** the desktop app. End users who install a built
package need nothing except the OS webview (see [Minimum OS versions](#minimum-os-versions)).

- **Rust toolchain** — install `rustup`, then the stable toolchain:
  ```bash
  rustup install stable
  ```
  Rust is required **at build time only**. There is no Rust at runtime, and
  contributors working solely on the Vue frontend or the ASP.NET Core backend do
  not need Rust at all.
- **Tauri CLI** — either the Cargo build or the npm devDependency (already listed
  in `desktop/package.json`):
  ```bash
  cargo install tauri-cli          # provides `cargo tauri ...`
  # or, from desktop/:
  npm install -D @tauri-apps/cli   # provides `npx tauri ...`
  ```
- **.NET 8 SDK** — to publish the backend sidecar.
- **Node.js 20+** — to build the frontend.

### Platform-specific build dependencies

- **Windows**
  - The **WebView2 Runtime** is pre-installed on Windows 10 1803+ and all
    Windows 11 machines. The installer bundles the WebView2 bootstrapper
    (`bundle.windows.webviewInstallMode = embedBootstrapper` in
    `tauri.conf.json`) so it also works on older Windows 10 builds.
- **macOS**
  - Install the Xcode Command Line Tools:
    ```bash
    xcode-select --install
    ```
  - Code signing / notarisation requires an Apple Developer account and is
    **out of scope** for now (see [Code signing](#code-signing--notarisation)).
    Unsigned builds work fine for personal / LAN use; Gatekeeper will ask you to
    approve the app on first launch (right-click → Open).
- **Linux**
  - Install the WebKitGTK webview and GTK dev headers plus packaging tools:
    ```bash
    # Debian / Ubuntu
    sudo apt install libwebkit2gtk-4.1-dev libgtk-3-dev \
      libayatana-appindicator3-dev librsvg2-dev patchelf
    ```
  - Minimum distro versions: **Ubuntu 22.04+**, **Fedora 37+** (these ship
    `webkit2gtk-4.1`).
  - WebKitGTK rendering can differ subtly from Windows/macOS. **Test the full UI
    on Linux before releasing.**

---

## Building

Run the platform build script from the `desktop/` directory. Each script builds
the frontend, publishes the backend sidecar, vendors the QR library, and builds
the Tauri installers.

```bash
# macOS / Linux
cd desktop
./build.sh
```

```powershell
# Windows
cd desktop
./build.ps1
```

Installers are written to `desktop/src-tauri/target/release/bundle/`:

| Platform | Output |
| -------- | ------ |
| Windows  | `msi/*.msi` and/or `nsis/*.exe` |
| macOS    | `dmg/*.dmg` |
| Linux    | `appimage/*.AppImage` and `deb/*.deb` |

### Running in development

With the toolchain installed you can run the app against the live Vite dev
server (hot reload):

```bash
# Terminal 1 — start the backend (see getting-started.md) on http://localhost:5000
# Terminal 2 — start the frontend dev server on http://localhost:5173
cd src/frontend/aircane-web && npm run dev
# Terminal 3 — start the desktop shell
cd desktop && cargo tauri dev
```

In `dev` mode the wrapper still starts its own backend sidecar and waits for
`/api/health`. If you prefer to run the backend yourself, set the port with the
`AIRCANE_PORT` env var so the wrapper and your backend agree.

> **QR window in dev:** the tray "Show QR Code" window loads a vendored
> `qrcode.min.js` from the frontend's `public/desktop/` folder. The build
> scripts copy it automatically; if you run `cargo tauri dev` directly, copy it
> once first:
> ```bash
> cp src/frontend/aircane-web/node_modules/qrcode/build/qrcode.min.js \
>    src/frontend/aircane-web/public/desktop/qrcode.min.js
> ```

---

## Target-triple ↔ .NET RID mapping

The backend is published as a self-contained, single-file executable and renamed
to Tauri's sidecar convention: `aircane-server-<rust-target-triple>[.exe]`.
Tauri appends the current target triple automatically at runtime, so the file
name must match. The build scripts derive the Rust host triple with
`rustc -vV` and map it to the matching .NET Runtime Identifier (RID):

| Rust target triple            | .NET RID     | Sidecar file name                             |
| ----------------------------- | ------------ | --------------------------------------------- |
| `x86_64-pc-windows-msvc`      | `win-x64`    | `aircane-server-x86_64-pc-windows-msvc.exe`   |
| `aarch64-pc-windows-msvc`     | `win-arm64`  | `aircane-server-aarch64-pc-windows-msvc.exe`  |
| `x86_64-apple-darwin`         | `osx-x64`    | `aircane-server-x86_64-apple-darwin`          |
| `aarch64-apple-darwin`        | `osx-arm64`  | `aircane-server-aarch64-apple-darwin`         |
| `x86_64-unknown-linux-gnu`    | `linux-x64`  | `aircane-server-x86_64-unknown-linux-gnu`     |
| `aarch64-unknown-linux-gnu`   | `linux-arm64`| `aircane-server-aarch64-unknown-linux-gnu`    |

The publish command each script runs:

```bash
dotnet publish src/backend/Aircane.Api/Aircane.Api.csproj \
  -c Release \
  -r <RID> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o desktop/src-tauri/binaries/
```

The generated binaries and Rust `target/` output are gitignored; only
`desktop/src-tauri/binaries/README.md` is tracked.

---

## Version synchronisation

The app version has a single source of truth: the [`VERSION`](../../VERSION) file
at the repo root. It flows to the places that need it:

- `src/backend/Aircane.Api/Aircane.Api.csproj` → `<Version>` reads the file at
  build time (with a `0.0.0` fallback if the file is missing).
- `src/frontend/aircane-web/package.json`, `desktop/package.json`, and
  `desktop/src-tauri/tauri.conf.json` → these need a literal version string (Tauri
  parses a file-path `version` value as JSON, which a plain `VERSION` file is
  not), so a script keeps them in sync.

To bump the version, run the script (it edits `VERSION`, both `package.json`
files, and `tauri.conf.json`); the csproj picks it up automatically at build:

```bash
scripts/bump-version.sh 0.2.0          # macOS / Linux
pwsh scripts/bump-version.ps1 0.2.0    # Windows
```

A version-bump PR should therefore change `VERSION`, the two `package.json`
files, and `tauri.conf.json`.

---

## Icons

Placeholder icons live in [`desktop/icons/`](../../desktop/icons/): a generic
white "A" on a dark indigo→sky rounded square. They are intentionally
brand-neutral and contain **no** D&D, Pathfinder, or Paizo imagery. Regenerate
them with:

```bash
python desktop/icons/generate_placeholder_icons.py
```

Once final branding and the Rust toolchain are available, the standard pipeline
regenerates every size from a single 1024×1024 source:

```bash
cargo tauri icon desktop/icons/icon.png
```

The system tray uses `desktop/icons/tray.png`, a 32×32 monochrome variant.

---

## Minimum OS versions

| Platform | Minimum version        | Webview                                   | Notes |
| -------- | ---------------------- | ----------------------------------------- | ----- |
| Windows  | 10 1803 (build 17134)  | WebView2 (built-in on Win10 20H2+, Win11) | Installer bundles the WebView2 bootstrapper for older Win10. |
| macOS    | 11 Big Sur             | WKWebView                                 | Apple Silicon (M1+) and Intel both supported. |
| Linux    | Ubuntu 22.04 / Fedora 37 | WebKitGTK 4.1                           | AppImage for broad distro support; test on both AppImage and `.deb`. Rendering may differ from Windows/macOS. |

---

## Code signing / notarisation

Deferred until distribution is planned. Unsigned builds work for personal and
LAN use:

- **Windows:** SmartScreen may warn on first run of an unsigned `.exe`/`.msi`
  ("More info" → "Run anyway").
- **macOS:** Gatekeeper blocks unsigned apps by default. Right-click the app →
  **Open** to approve it once, or run
  `xattr -dr com.apple.quarantine "/Applications/Aircane Tabletop.app"`.
  Distribution outside a personal machine requires an Apple Developer account,
  signing, and notarisation.
- **Linux:** AppImage/`.deb` are unaffected.

Auto-update (`tauri-plugin-updater`) is also deferred to a separate task once the
release pipeline is established.

---

## Continuous integration

[`.github/workflows/desktop.yml`](../../.github/workflows/desktop.yml) builds the
app on `windows-latest`, `macos-latest`, and `ubuntu-latest` on pushes to `main`
(touching desktop/backend/frontend paths) and via manual `workflow_dispatch`.
It installs Rust, .NET 8, and Node 20, runs the platform build script, and
**uploads the installers as artifacts**. It never creates a GitHub Release —
release tagging is a deliberate manual step.

---

## Smoke test checklist

Run these manually on each platform before a release. A green CI build proves the
app **compiles and packages**; it does not prove the runtime behaviour below.

```text
Desktop wrapper smoke tests (run manually per platform before release):

[ ] Double-click installer -> app opens, loading screen shows
[ ] Backend health check passes -> webview navigates to Aircane UI
[ ] Window title shows local and LAN URLs after session starts
[ ] Tray icon appears; Copy LAN URL works; QR code window opens
[ ] Ollama not running + Ollama configured -> banner appears with correct copy commands
[ ] Ollama running -> no banner; Settings shows green status
[ ] Port 5000 conflict -> clear error message, not a hang
[ ] Close window -> sidecar process exits (verify in Task Manager / Activity Monitor)
[ ] Restart app -> backend starts cleanly (no port-in-use error from previous run)
[ ] LAN player connects via browser -> session works identically to browser-only mode
```

---

## Architecture notes

- The webview points at the **same URLs the browser would use**
  (`http://localhost:<port>`). No Tauri-specific frontend code is needed for the
  core UI. Only the Ollama banner and the tray/navigation event listeners are
  Tauri-aware, and they are all guarded by a `window.__TAURI__` check
  (`src/shared/tauri/bridge.ts`), so the Vue app still runs in a plain browser.
- The backend binds `http://0.0.0.0:<port>` so LAN players can reach the host;
  the webview and health probe use `http://localhost:<port>`.
- The port is configurable via the `AIRCANE_PORT` environment variable or a
  `config.json` in the app config directory; both the sidecar launch
  (`ASPNETCORE_URLS`) and the webview navigation read the same value.
- LAN IP detection lives **server-side** at `GET /api/sessions/network-info`
  (returns `{ localUrl, lanUrl, inviteCode }`) so the browser UI and the desktop
  wrapper share one source of truth. `inviteCode` is always null from that
  endpoint — invite codes are returned only once, at session creation.
- Startup logs (including startup failures) are written to the platform log
  directory; the error screen's "View logs" button opens it.
- The sidecar is stopped on the main window's close, on app exit, and before any
  restart. **Known edge case:** if the wrapper process is force-killed or panics,
  the OS-level handlers don't run and the backend can linger holding the port; the
  next launch then detects a port conflict and shows the actionable error screen
  (set `AIRCANE_PORT` or stop the stray process). Tying the child's lifetime to
  the parent via a Windows Job Object / Unix process group is tracked as future
  hardening.
- Port-conflict detection: during startup the wrapper polls `/api/health`. Any
  well-formed HTTP response that is **not** Aircane's health shape (regardless of
  status code) is treated as a foreign server owning the port, producing the
  "port in use" error rather than a 30-second hang.

### Not bundled

- **Ollama and LLM model weights** are never bundled (Ollama ~200 MB; models
  2–4 GB each, with their own release cadence). The app guides the user to
  install them instead — see [ai-configuration.md](./ai-configuration.md).

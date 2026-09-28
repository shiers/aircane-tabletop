//! Aircane Tabletop desktop wrapper (Tauri v2).
//!
//! Responsibilities:
//!   1. Start the ASP.NET Core backend as a managed sidecar.
//!   2. Show a loading screen, then navigate the main window to the backend once
//!      `/api/health` is ready.
//!   3. Show a clear error page on timeout / port conflict, with Retry + View logs.
//!   4. Surface an Ollama prompt to the frontend when Ollama is the active provider
//!      but the daemon isn't running.
//!   5. Set the window title to the local + LAN URLs and build a system tray.
//!   6. Kill the sidecar cleanly when the app exits.

mod config;
mod network;
mod ollama;
mod sidecar;
mod tray;

use std::io::Write;

use tauri::{AppHandle, Emitter, Manager, WindowEvent};
use tauri_plugin_opener::OpenerExt;

use config::DesktopConfig;
use sidecar::{ReadyResult, SidecarState};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_opener::init())
        .manage(SidecarState::default())
        .invoke_handler(tauri::generate_handler![
            retry_startup,
            open_logs,
            open_settings,
            open_external,
            get_base_url,
            check_ollama_status,
        ])
        .setup(|app| {
            let handle = app.handle().clone();

            // Resolve the effective port config (env var > config.json > default).
            let config_dir = app.path().app_config_dir().ok();
            let cfg = DesktopConfig::resolve(config_dir.as_ref());
            app.manage(cfg.clone());

            // Build the tray immediately so it's present even during startup.
            if let Err(e) = tray::build_tray(&handle) {
                log::warn!("Failed to build system tray: {e}");
            }

            // Kick off the startup sequence asynchronously so the loading window
            // (already visible via config) stays responsive.
            let startup_handle = handle.clone();
            tauri::async_runtime::spawn(async move {
                run_startup(startup_handle).await;
            });

            // The main window is created hidden (visible:false) pointing at
            // loading.html. Show it now so the user sees the spinner immediately.
            if let Some(main) = app.get_webview_window("main") {
                let _ = main.show();
            }

            Ok(())
        })
        .on_window_event(|window, event| {
            // When the main window is closing, tear the sidecar down. We only act
            // on the "main" window so closing the QR popup doesn't kill the server.
            if window.label() == "main" {
                if let WindowEvent::CloseRequested { .. } = event {
                    sidecar::stop_sidecar(window.app_handle());
                }
            }
        })
        .build(tauri::generate_context!())
        .expect("error while building the Aircane Tabletop desktop app")
        .run(|app_handle, event| {
            // Final safety net: ensure the sidecar dies if the app exits for any reason.
            if let tauri::RunEvent::ExitRequested { .. } = event {
                sidecar::stop_sidecar(app_handle);
            }
        });
}

/// The full startup sequence: spawn sidecar → wait for health → navigate or error.
async fn run_startup(app: AppHandle) {
    let cfg = app.state::<DesktopConfig>().inner().clone();

    // 1. Spawn the backend.
    if let Err(e) = sidecar::start_sidecar(&app, &cfg) {
        log::error!("Sidecar failed to start: {e}");
        navigate_to_error(&app, "timeout", &e);
        return;
    }

    // 2. Wait until the backend answers /api/health (or time out / detect conflict).
    match sidecar::wait_until_ready(&cfg).await {
        ReadyResult::Ready => {
            navigate_main_to_backend(&app, &cfg);
            // 3. After the UI is up, run the post-ready checks.
            post_ready(app.clone(), cfg.clone()).await;
        }
        ReadyResult::PortConflict => {
            let detail = format!(
                "Port {} is already in use. Another application may be running on this port.",
                cfg.port
            );
            log::error!("{detail}");
            navigate_to_error(&app, "port-conflict", &detail);
        }
        ReadyResult::Timeout => {
            let detail = format!(
                "The backend did not become ready within 30 seconds on port {}.",
                cfg.port
            );
            log::error!("{detail}");
            navigate_to_error(&app, "timeout", &detail);
        }
    }
}

/// Post-startup tasks: set the window title from network info, and run the Ollama check.
async fn post_ready(app: AppHandle, cfg: DesktopConfig) {
    // Window title with local + LAN URLs.
    update_window_title(&app, &cfg).await;

    // Ollama check — only when Ollama is the configured provider.
    if ollama::is_ollama_the_active_provider(&cfg).await {
        let running = ollama::check_ollama().await;
        if !running {
            log::info!("Ollama is the active provider but is not running; notifying frontend.");
            let _ = app.emit("ollama://not-running", ());
        }
    }
}

async fn update_window_title(app: &AppHandle, cfg: &DesktopConfig) {
    let info = network::fetch_with_config(cfg).await;
    let local = info
        .as_ref()
        .and_then(|i| i.local_url.clone())
        .unwrap_or_else(|| cfg.base_url());
    let title = match info.as_ref().and_then(|i| i.lan_url.clone()) {
        Some(lan) => format!("Aircane Tabletop — Local: {local} — LAN: {lan}"),
        None => format!("Aircane Tabletop — Local: {local}"),
    };
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.set_title(&title);
    }
}

fn navigate_main_to_backend(app: &AppHandle, cfg: &DesktopConfig) {
    if let Some(window) = app.get_webview_window("main") {
        match cfg.base_url().parse() {
            Ok(url) => {
                let _ = window.navigate(url);
            }
            Err(e) => log::error!("Invalid backend URL: {e}"),
        }
    }
}

fn navigate_to_error(app: &AppHandle, reason: &str, detail: &str) {
    // Write the detail to the log directory so "View logs" has something to show.
    write_startup_log(app, detail);

    if let Some(window) = app.get_webview_window("main") {
        // Navigate between bundled assets via a relative location change so the
        // asset origin resolves correctly on every platform (the custom protocol
        // host differs: tauri.localhost on Windows, tauri://localhost elsewhere).
        let encoded_detail = url_encode(detail);
        let script = format!(
            "window.location.replace('desktop/error.html?reason={reason}&detail={encoded_detail}');"
        );
        let _ = window.eval(&script);
        let _ = window.show();
    }
}

fn write_startup_log(app: &AppHandle, message: &str) {
    if let Ok(dir) = app.path().app_log_dir() {
        let _ = std::fs::create_dir_all(&dir);
        let path = dir.join("startup.log");
        if let Ok(mut file) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
        {
            let _ = writeln!(
                file,
                "[{}] {message}",
                chrono_like_timestamp()
            );
        }
    }
}

/// A tiny timestamp without pulling in the `chrono` crate.
fn chrono_like_timestamp() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    format!("unix:{secs}")
}

fn url_encode(input: &str) -> String {
    input
        .bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                (b as char).to_string()
            }
            _ => format!("%{b:02X}"),
        })
        .collect()
}

// ── Commands invoked from the error page and the frontend ──────────────────────

/// Retry the whole startup sequence (used by the error page "Retry" button).
#[tauri::command]
async fn retry_startup(app: AppHandle) {
    // Reset the main window to the loading screen, then re-run startup.
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.eval("window.location.replace('desktop/loading.html');");
    }
    run_startup(app).await;
}

/// Open the log directory in the OS file manager (used by "View logs").
#[tauri::command]
fn open_logs(app: AppHandle) -> Result<(), String> {
    let dir = app
        .path()
        .app_log_dir()
        .map_err(|e| format!("Could not resolve log directory: {e}"))?;
    let _ = std::fs::create_dir_all(&dir);
    app.opener()
        .open_path(dir.to_string_lossy(), None::<&str>)
        .map_err(|e| format!("Could not open log directory: {e}"))
}

/// Ask the frontend to navigate to the in-app AI provider settings.
///
/// Emits an event and lets Vue Router handle the client-side navigation rather
/// than doing a full-page navigate to a deep link, which would depend on the
/// backend's SPA fallback being configured for arbitrary history-mode paths.
/// Only meaningful once the webview has navigated to the running backend (i.e.
/// the Vue app is mounted to receive the event).
#[tauri::command]
fn open_settings(app: AppHandle) {
    let _ = app.emit("navigate://ai-settings", ());
}

/// Open a URL in the system browser (not the webview). Used by "Start Ollama".
#[tauri::command]
fn open_external(app: AppHandle, url: String) -> Result<(), String> {
    app.opener()
        .open_url(url, None::<&str>)
        .map_err(|e| format!("Could not open URL: {e}"))
}

/// Returns the resolved backend base URL so the frontend can talk to the sidecar
/// directly (bypassing the dev proxy) when running inside Tauri.
#[tauri::command]
fn get_base_url(app: AppHandle) -> String {
    app.state::<DesktopConfig>().inner().base_url()
}

/// Checks whether the Ollama daemon is reachable. Used by the AI settings screen
/// to show a live status indicator. Runs from Rust to avoid a browser
/// cross-origin request to localhost:11434.
#[tauri::command]
async fn check_ollama_status() -> bool {
    ollama::check_ollama().await
}

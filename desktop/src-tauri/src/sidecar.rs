//! ASP.NET Core backend process management.
//!
//! The backend ships as a self-contained single-file executable and is run as a
//! Tauri *sidecar*. This module owns its lifecycle: spawn on startup, wait until
//! `/api/health` answers, and kill it cleanly when the app exits.

use std::path::Path;
use std::sync::Mutex;
use std::time::Duration;

use tauri::Manager;
use tauri_plugin_shell::process::Command;
use tauri_plugin_shell::process::CommandChild;
use tauri_plugin_shell::ShellExt;

use crate::config::DesktopConfig;

/// Holds the running sidecar child so it can be killed on shutdown.
#[derive(Default)]
pub struct SidecarState(pub Mutex<Option<CommandChild>>);

/// Outcome of the readiness poll after spawning the sidecar.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ReadyResult {
    /// `/api/health` returned a valid Aircane health response.
    Ready,
    /// The 30s window elapsed without a healthy response.
    Timeout,
    /// Something answered on the port but it was not Aircane (port conflict),
    /// or the sidecar could not bind the port at all.
    PortConflict,
}

/// Spawns the backend sidecar. Environment variables from the host process are
/// inherited automatically, so OS-level API keys pass through. We additionally
/// pin the environment name and the URL the server binds to.
pub fn start_sidecar(app: &tauri::AppHandle, config: &DesktopConfig) -> Result<(), String> {
    // If a previous child is somehow still tracked, kill it before starting a new one
    // so a Retry never leaves two servers fighting over the port.
    stop_sidecar(app);

    let command = app
        .shell()
        .sidecar("aircane-server")
        .map_err(|e| format!("Failed to locate sidecar binary: {e}"))?
        .env("ASPNETCORE_ENVIRONMENT", "Production")
        .env("ASPNETCORE_URLS", config.aspnetcore_urls())
        // The single local user running the desktop app IS the host. Trust them
        // so host-only surfaces (AI settings, session creation) work without a
        // session-scoped token. ASP.NET Core maps the double-underscore env var
        // to the Aircane:TrustLocalHost config key. NEVER set this for a shared
        // or hosted backend.
        .env("Aircane__TrustLocalHost", "true");

    // Point the backend at the bundled OCR assets so OCR works with zero user
    // setup under the desktop app. The tessdata model and optional native libs
    // are copied into the Tauri resource dir by the `bundle.resources` map; an
    // explicit `Ocr__TessdataPath` env var wins over the appsettings default.
    // When NOT launched by Tauri (dev/server), no env var is set and the backend
    // falls back to its configured TessdataPath / AutoDownloadTessdata behaviour.
    let command = if let Ok(dir) = app.path().resource_dir() {
        let tessdata = dir.join("tessdata");
        let native = dir.join("ocr-native");
        let command =
            command.env("Ocr__TessdataPath", tessdata.to_string_lossy().to_string());
        // Prepend the bundled native OCR lib dir to the OS loader search path so
        // the sidecar finds libtesseract/libleptonica (a harmless no-op when the
        // dir is empty, e.g. Windows where the native libs ship with the publish).
        prepend_lib_path(command, &native)
    } else {
        command
    };

    let child = command
        .spawn()
        .map_err(|e| format!("Failed to spawn backend sidecar: {e}"))?
        .1;

    app.state::<SidecarState>()
        .0
        .lock()
        .map_err(|_| "Sidecar state lock poisoned".to_string())?
        .replace(child);

    Ok(())
}

/// Prepend `dir` to the OS dynamic-loader search path so the sidecar finds the bundled
/// native OCR libs. The `tauri_plugin_shell` `Command` builder is consumed by value in
/// the `.env(...)` chain, so this takes and returns a `Command`. It reads the current
/// process's value of the per-OS loader var, prepends `dir` with the platform separator,
/// and threads the updated builder back through `.env(...)`.
///   Windows: PATH ; Linux: LD_LIBRARY_PATH ; macOS: DYLD_LIBRARY_PATH
fn prepend_lib_path(cmd: Command, dir: &Path) -> Command {
    #[cfg(windows)]
    let var = "PATH";
    #[cfg(target_os = "linux")]
    let var = "LD_LIBRARY_PATH";
    #[cfg(target_os = "macos")]
    let var = "DYLD_LIBRARY_PATH";

    let sep = if cfg!(windows) { ';' } else { ':' };
    let dir_str = dir.to_string_lossy();
    let new_val = match std::env::var(var) {
        Ok(existing) if !existing.is_empty() => format!("{dir_str}{sep}{existing}"),
        _ => dir_str.to_string(),
    };
    cmd.env(var, new_val)
}

/// Kills the tracked sidecar child, if any. Safe to call multiple times.
pub fn stop_sidecar(app: &tauri::AppHandle) {
    if let Some(state) = app.try_state::<SidecarState>() {
        if let Ok(mut guard) = state.0.lock() {
            if let Some(child) = guard.take() {
                let _ = child.kill();
            }
        }
    }
}

/// Polls the backend health endpoint every 200ms for up to 30 seconds.
///
/// Returns:
/// - `Ready` once `/api/health` returns a JSON body that has a `status` field
///   with one of the expected Aircane values (`healthy` / `degraded` /
///   `unhealthy`). The backend returns 200 for healthy/degraded and 503 for
///   unhealthy, so we accept any of those status codes as long as the body
///   shape matches — the app is reachable and it is genuinely Aircane.
/// - `PortConflict` if a response comes back that does NOT look like Aircane
///   (a foreign server is already on the port).
/// - `Timeout` if nothing answered within the window.
pub async fn wait_until_ready(config: &DesktopConfig) -> ReadyResult {
    let client = reqwest::Client::builder()
        .timeout(Duration::from_millis(1000))
        .build()
        .expect("failed to build reqwest client");

    let health_url = config.health_url();
    let deadline = std::time::Instant::now() + Duration::from_secs(30);
    let mut saw_foreign_response = false;

    while std::time::Instant::now() < deadline {
        match client.get(&health_url).send().await {
            Ok(response) => {
                let status = response.status();
                // Parse the body regardless of status. Aircane's health endpoint
                // returns 200 (healthy/degraded) or 503 (unhealthy), and BOTH carry
                // the health JSON shape — so a body that matches means it's us and
                // we're reachable.
                match response.json::<serde_json::Value>().await {
                    Ok(body) if is_aircane_health(&body) => return ReadyResult::Ready,
                    // A well-formed JSON response that is NOT Aircane's health shape
                    // means some other service owns this port. This holds regardless
                    // of status code — foreign servers commonly answer 401/404/500,
                    // not just 2xx. We key off the Aircane body shape (not the status)
                    // so our own server briefly 500ing mid-boot is not misread as a
                    // conflict: a 500 with no/!Aircane body simply doesn't match, and
                    // we keep polling; a *foreign* server's response also won't match
                    // and flags the conflict. Since our health endpoint always returns
                    // the health shape once the app is up, any parseable non-Aircane
                    // JSON is evidence of a foreign server.
                    Ok(_) => {
                        saw_foreign_response = true;
                    }
                    // A non-JSON / unparseable body is treated as foreign only when
                    // the responder returned 2xx (e.g. another app serving an HTML
                    // page on this port). We stay conservative for non-2xx non-JSON
                    // so a transient framework-level error during our own slow boot
                    // is never misread as a conflict — those keep us polling.
                    Err(_) if status.is_success() => {
                        saw_foreign_response = true;
                    }
                    Err(_) => {}
                }
            }
            // Connection refused is expected while the server is still starting.
            Err(_) => {}
        }

        tokio::time::sleep(Duration::from_millis(200)).await;
    }

    if saw_foreign_response {
        ReadyResult::PortConflict
    } else {
        ReadyResult::Timeout
    }
}

/// Returns true if the JSON body looks like Aircane's health response.
/// The backend returns `{ "status": "healthy" | "degraded" | "unhealthy", ... }`.
fn is_aircane_health(body: &serde_json::Value) -> bool {
    body.get("status")
        .and_then(|s| s.as_str())
        .map(|s| matches!(s, "healthy" | "degraded" | "unhealthy"))
        .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn recognises_aircane_health_shape() {
        assert!(is_aircane_health(&json!({ "status": "healthy" })));
        assert!(is_aircane_health(&json!({ "status": "degraded", "api": {} })));
        assert!(is_aircane_health(&json!({ "status": "unhealthy" })));
    }

    #[test]
    fn rejects_foreign_responses() {
        assert!(!is_aircane_health(&json!({ "message": "hello from nginx" })));
        assert!(!is_aircane_health(&json!({ "status": "ok" }))); // not an Aircane value
        assert!(!is_aircane_health(&json!("plain string")));
    }
}

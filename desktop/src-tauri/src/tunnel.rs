//! Cloudflare Tunnel (internet play) lifecycle.
//!
//! The `cloudflared` binary ships as a second Tauri sidecar. When the host enables
//! internet play, we spawn `cloudflared tunnel --url http://localhost:<port>`, read
//! its stderr until it prints the temporary public URL
//! (`https://<something>.trycloudflare.com`), and report that URL to the backend so
//! it can enter "internet mode" (rate limiting + CSRF hardening) and surface the URL
//! in network-info / join links.
//!
//! The free tunnel issues a new URL every session, so this state is entirely
//! in-memory and torn down when the tunnel stops or the app exits.

use std::sync::Mutex;
use std::time::{Duration, Instant};

use tauri::Manager;
use tauri_plugin_shell::process::{CommandChild, CommandEvent};
use tauri_plugin_shell::ShellExt;

use crate::config::DesktopConfig;

/// Holds the running `cloudflared` child and the public URL it produced.
#[derive(Default)]
pub struct TunnelState(pub Mutex<Option<TunnelInfo>>);

pub struct TunnelInfo {
    pub child: CommandChild,
    pub public_url: String,
}

/// How long to wait for cloudflared to print its public URL before giving up.
const TUNNEL_START_TIMEOUT: Duration = Duration::from_secs(30);

/// Starts the Cloudflare tunnel pointing at the local backend port and returns the
/// public URL once cloudflared reports it. Reports the URL to the backend on success.
pub async fn start_tunnel(app: &tauri::AppHandle, local_port: u16) -> Result<String, String> {
    // If a tunnel is already running, tear it down first so we never leak a child.
    stop_tunnel(app).await;

    let (mut rx, child) = app
        .shell()
        .sidecar("cloudflared")
        .map_err(|e| format!("Failed to locate cloudflared sidecar: {e}"))?
        .args([
            "tunnel",
            "--no-autoupdate",
            "--url",
            &format!("http://localhost:{local_port}"),
        ])
        .spawn()
        .map_err(|e| format!("Failed to spawn cloudflared: {e}"))?;

    // cloudflared prints the assigned public URL to stderr. Read events until we
    // see a trycloudflare.com URL or the timeout elapses.
    let start = Instant::now();
    let mut child = Some(child);

    while start.elapsed() < TUNNEL_START_TIMEOUT {
        match tokio::time::timeout(Duration::from_millis(500), rx.recv()).await {
            Ok(Some(event)) => {
                let line = match event {
                    CommandEvent::Stderr(bytes) | CommandEvent::Stdout(bytes) => {
                        String::from_utf8_lossy(&bytes).into_owned()
                    }
                    CommandEvent::Terminated(payload) => {
                        // cloudflared exited before yielding a URL — surface a clear error.
                        return Err(format!(
                            "cloudflared exited before establishing a tunnel (code {:?}).",
                            payload.code
                        ));
                    }
                    _ => continue,
                };

                if let Some(url) = extract_trycloudflare_url(&line) {
                    let child = child.take().expect("child taken only once");
                    app.state::<TunnelState>()
                        .0
                        .lock()
                        .map_err(|_| "Tunnel state lock poisoned".to_string())?
                        .replace(TunnelInfo {
                            child,
                            public_url: url.clone(),
                        });

                    log::info!("Cloudflare tunnel established: {url}");

                    // Best-effort: tell the backend to enter internet mode. A failure
                    // here does not invalidate the tunnel itself, but we log it.
                    report_tunnel_url_to_backend(app, &url).await;

                    return Ok(url);
                }
            }
            // No event this interval; keep waiting until the deadline.
            Ok(None) => break,
            Err(_) => continue,
        }
    }

    // Timed out (or the event stream closed) without a URL. Kill the child we spawned.
    if let Some(child) = child.take() {
        let _ = child.kill();
    }
    Err("Tunnel did not start within 30 seconds".into())
}

/// Stops the running tunnel (if any) and notifies the backend to leave internet mode.
pub async fn stop_tunnel(app: &tauri::AppHandle) {
    let info = app
        .try_state::<TunnelState>()
        .and_then(|state| state.0.lock().ok().and_then(|mut guard| guard.take()));

    if let Some(info) = info {
        let _ = info.child.kill();
        log::info!("Cloudflare tunnel stopped.");
        // Best-effort: clear the backend's internet mode.
        clear_tunnel_url_on_backend(app).await;
    }
}

/// Synchronous best-effort stop for shutdown paths (window close / app exit) where
/// awaiting is inconvenient. Kills the child; the backend clears its own in-memory
/// tunnel state on the next start or restart.
pub fn stop_tunnel_blocking(app: &tauri::AppHandle) {
    if let Some(state) = app.try_state::<TunnelState>() {
        if let Ok(mut guard) = state.0.lock() {
            if let Some(info) = guard.take() {
                let _ = info.child.kill();
                log::info!("Cloudflare tunnel stopped (shutdown).");
            }
        }
    }
}

/// Extracts the first `https://<sub>.trycloudflare.com` URL from a log line, if present.
/// Avoids a regex dependency: scans for the scheme and consumes valid host characters
/// until the `trycloudflare.com` suffix is confirmed.
fn extract_trycloudflare_url(line: &str) -> Option<String> {
    const SCHEME: &str = "https://";
    let scheme_at = line.find(SCHEME)?;
    let host_start = scheme_at + SCHEME.len();
    let host_part = &line[host_start..];

    // Consume characters valid in the host portion of the URL (after the scheme).
    let host_len = host_part
        .char_indices()
        .find(|&(_, c)| !is_url_host_char(c))
        .map(|(i, _)| i)
        .unwrap_or(host_part.len());

    let candidate = &line[scheme_at..host_start + host_len];
    if candidate.ends_with(".trycloudflare.com") {
        Some(candidate.to_string())
    } else {
        None
    }
}

fn is_url_host_char(c: char) -> bool {
    c.is_ascii_alphanumeric() || matches!(c, '-' | '.' )
}

async fn report_tunnel_url_to_backend(app: &tauri::AppHandle, url: &str) {
    let base = app.state::<DesktopConfig>().inner().base_url();
    let endpoint = format!("{base}/api/sessions/tunnel-url");

    let client = match reqwest::Client::builder()
        .timeout(Duration::from_millis(3000))
        .build()
    {
        Ok(c) => c,
        Err(e) => {
            log::warn!("Failed to build HTTP client to report tunnel URL: {e}");
            return;
        }
    };

    let body = serde_json::json!({ "tunnelUrl": url });
    match client.post(&endpoint).json(&body).send().await {
        Ok(resp) if resp.status().is_success() => {
            log::info!("Reported tunnel URL to backend.");
        }
        Ok(resp) => {
            log::warn!("Backend rejected tunnel URL report: HTTP {}", resp.status());
        }
        Err(e) => {
            log::warn!("Failed to report tunnel URL to backend: {e}");
        }
    }
}

async fn clear_tunnel_url_on_backend(app: &tauri::AppHandle) {
    let base = app.state::<DesktopConfig>().inner().base_url();
    let endpoint = format!("{base}/api/sessions/tunnel-url");

    let client = match reqwest::Client::builder()
        .timeout(Duration::from_millis(3000))
        .build()
    {
        Ok(c) => c,
        Err(_) => return,
    };

    // DELETE clears the backend's in-memory tunnel state (leaves internet mode).
    let _ = client.delete(&endpoint).send().await;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_url_from_cloudflared_line() {
        let line = "2024-01-01T00:00:00Z INF +----+ |  https://happy-cat-1234.trycloudflare.com  | +----+";
        assert_eq!(
            extract_trycloudflare_url(line),
            Some("https://happy-cat-1234.trycloudflare.com".to_string())
        );
    }

    #[test]
    fn extracts_plain_url() {
        let line = "https://abc-def-ghi.trycloudflare.com";
        assert_eq!(
            extract_trycloudflare_url(line),
            Some("https://abc-def-ghi.trycloudflare.com".to_string())
        );
    }

    #[test]
    fn ignores_non_trycloudflare_urls() {
        assert_eq!(extract_trycloudflare_url("https://example.com/path"), None);
        assert_eq!(extract_trycloudflare_url("no url here"), None);
        assert_eq!(
            extract_trycloudflare_url("connecting to https://api.cloudflare.com"),
            None
        );
    }
}

//! Fetches LAN/network info from the backend for the window title and tray menu.
//!
//! The backend exposes `GET /api/sessions/network-info` (added by the desktop
//! wrapper work) returning `{ localUrl, lanUrl, inviteCode }`. LAN IP detection
//! lives server-side so both the browser UI and the desktop wrapper share one
//! source of truth.

use std::time::Duration;

use serde::Deserialize;
use tauri::{AppHandle, Manager};

use crate::config::DesktopConfig;

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct NetworkInfo {
    pub local_url: Option<String>,
    pub lan_url: Option<String>,
    // Part of the endpoint's response contract; deserialized for completeness but
    // not consumed on the Rust side (the tray/title only use the URLs). The
    // endpoint always returns null here anyway — invite codes are creation-only.
    #[allow(dead_code)]
    pub invite_code: Option<String>,
    /// Public Cloudflare Tunnel URL when internet play is active; null otherwise.
    #[serde(default)]
    #[allow(dead_code)]
    pub tunnel_url: Option<String>,
    /// True when an internet tunnel is currently active.
    #[serde(default)]
    #[allow(dead_code)]
    pub tunnel_active: bool,
}

/// Fetches network info using the app's resolved config. Returns None on any error.
pub async fn fetch_network_info(app: &AppHandle) -> Option<NetworkInfo> {
    let config = app.state::<DesktopConfig>().inner().clone();
    fetch_with_config(&config).await
}

pub async fn fetch_with_config(config: &DesktopConfig) -> Option<NetworkInfo> {
    let url = format!("{}/api/sessions/network-info", config.base_url());
    let client = reqwest::Client::builder()
        .timeout(Duration::from_millis(2000))
        .build()
        .ok()?;

    let response = client.get(&url).send().await.ok()?;
    response.json::<NetworkInfo>().await.ok()
}

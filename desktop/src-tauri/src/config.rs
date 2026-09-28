//! Runtime configuration for the desktop wrapper.
//!
//! The port the backend listens on must be shared between the sidecar launch
//! command (`ASPNETCORE_URLS`) and the webview navigation URL. It is resolved,
//! in order of precedence:
//!   1. The `AIRCANE_PORT` environment variable (useful for CI / debugging).
//!   2. A `config.json` file in the app config directory (written when the user
//!      changes the port from Settings).
//!   3. The default, 5000.

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

pub const DEFAULT_PORT: u16 = 5000;
pub const DEFAULT_OLLAMA_PORT: u16 = 11434;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DesktopConfig {
    /// Port the ASP.NET Core backend binds to and the webview points at.
    #[serde(default = "default_port")]
    pub port: u16,
}

fn default_port() -> u16 {
    DEFAULT_PORT
}

impl Default for DesktopConfig {
    fn default() -> Self {
        Self {
            port: DEFAULT_PORT,
        }
    }
}

impl DesktopConfig {
    /// Resolves the effective config: env var overrides the file, file overrides default.
    pub fn resolve(config_dir: Option<&PathBuf>) -> Self {
        let mut config = config_dir
            .map(|dir| dir.join("config.json"))
            .and_then(|path| std::fs::read_to_string(path).ok())
            .and_then(|raw| serde_json::from_str::<DesktopConfig>(&raw).ok())
            .unwrap_or_default();

        if let Ok(env_port) = std::env::var("AIRCANE_PORT") {
            if let Ok(parsed) = env_port.parse::<u16>() {
                config.port = parsed;
            }
        }

        config
    }

    /// Base URL the webview navigates to once the backend is ready.
    pub fn base_url(&self) -> String {
        format!("http://localhost:{}", self.port)
    }

    /// The value passed to the sidecar as `ASPNETCORE_URLS`.
    /// Binds to all interfaces so LAN players can reach the host.
    pub fn aspnetcore_urls(&self) -> String {
        format!("http://0.0.0.0:{}", self.port)
    }

    /// The health endpoint used as the readiness probe.
    pub fn health_url(&self) -> String {
        format!("http://localhost:{}/api/health", self.port)
    }
}

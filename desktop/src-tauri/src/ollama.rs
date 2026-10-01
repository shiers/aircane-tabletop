//! Ollama awareness.
//!
//! Ollama is optional but is the recommended way to get real local AI quality.
//! In a desktop context the user has no terminal open, so we detect whether the
//! Ollama daemon is reachable and — only when Ollama is the configured provider —
//! surface a friendly prompt in the UI via a Tauri event.

use std::time::Duration;

use crate::config::{DesktopConfig, DEFAULT_OLLAMA_PORT};

/// `AiProviderType.Ollama` on the wire is the integer `4` (see the frontend enum
/// and `AiProviderType` in the backend). We compare against the numeric value the
/// `/api/ai/settings` endpoint returns for `activeProvider`.
const AI_PROVIDER_OLLAMA: i64 = 4;

/// Returns true if the Ollama daemon answers on its default port.
pub async fn check_ollama() -> bool {
    let url = format!("http://localhost:{DEFAULT_OLLAMA_PORT}");
    let client = match reqwest::Client::builder()
        .timeout(Duration::from_millis(1500))
        .build()
    {
        Ok(c) => c,
        Err(_) => return false,
    };

    client
        .get(&url)
        .send()
        .await
        .map(|r| r.status().is_success())
        .unwrap_or(false)
}

/// Reads the active AI provider from the backend and returns true if it is Ollama.
/// If the settings call fails for any reason we return false (skip the check)
/// rather than nagging the user with a banner they can't act on.
pub async fn is_ollama_the_active_provider(config: &DesktopConfig) -> bool {
    let url = format!("{}/api/ai/settings", config.base_url());
    let client = match reqwest::Client::builder()
        .timeout(Duration::from_millis(2000))
        .build()
    {
        Ok(c) => c,
        Err(_) => return false,
    };

    match client.get(&url).send().await {
        Ok(response) => match response.json::<serde_json::Value>().await {
            Ok(body) => body
                .get("activeProvider")
                .and_then(|v| v.as_i64())
                .map(|v| v == AI_PROVIDER_OLLAMA)
                .unwrap_or(false),
            Err(_) => false,
        },
        Err(_) => false,
    }
}

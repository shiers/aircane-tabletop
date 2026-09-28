//! System tray.
//!
//! Lets the host minimise the main window while players can still grab the LAN
//! join URL. Menu:
//!   - Copy LAN URL   → copies http://<lan-ip>:<port> to the clipboard
//!   - Show QR Code   → opens a small floating window with the LAN URL QR
//!   - Open Aircane   → focuses the main window
//!   - Quit           → stops the sidecar and exits

use tauri::menu::{Menu, MenuItem};
use tauri::tray::{TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder};

use crate::network;

pub const TRAY_ID: &str = "aircane-tray";

/// The monochrome tray icon, embedded at compile time and decoded into an owned
/// (`'static`) image. The bytes are compiled into the binary, so decoding cannot
/// fail at runtime for a valid committed PNG.
fn tray_icon() -> tauri::image::Image<'static> {
    const TRAY_PNG: &[u8] = include_bytes!("../icons/tray.png");
    tauri::image::Image::from_bytes(TRAY_PNG)
        .expect("embedded tray icon (icons/tray.png) must be a valid PNG")
}

pub fn build_tray(app: &AppHandle) -> tauri::Result<()> {
    let copy_url = MenuItem::with_id(app, "copy_lan_url", "Copy LAN URL", true, None::<&str>)?;
    let copy_internet =
        MenuItem::with_id(app, "copy_internet_url", "Copy internet URL", true, None::<&str>)?;
    let show_qr = MenuItem::with_id(app, "show_qr", "Show QR Code", true, None::<&str>)?;
    let open = MenuItem::with_id(app, "open_main", "Open Aircane", true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;

    let menu = Menu::with_items(app, &[&copy_url, &copy_internet, &show_qr, &open, &quit])?;

    TrayIconBuilder::with_id(TRAY_ID)
        .icon(tray_icon())
        .tooltip("Aircane Tabletop")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(move |app, event| match event.id.as_ref() {
            "copy_lan_url" => {
                let app = app.clone();
                tauri::async_runtime::spawn(async move {
                    copy_lan_url(&app).await;
                });
            }
            "copy_internet_url" => copy_internet_url(app),
            "show_qr" => {
                let app = app.clone();
                tauri::async_runtime::spawn(async move {
                    show_qr_window(&app).await;
                });
            }
            "open_main" => focus_main(app),
            "quit" => {
                crate::sidecar::stop_sidecar(app);
                app.exit(0);
            }
            _ => {}
        })
        .on_tray_icon_event(|tray, event| {
            // Left-click brings the main window forward, a common convention.
            if let TrayIconEvent::Click { .. } = event {
                focus_main(tray.app_handle());
            }
        })
        .build(app)?;

    Ok(())
}

fn focus_main(app: &AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
}

/// Copies the active tunnel's public URL, if a tunnel is running. When no tunnel
/// is active it emits an event carrying `None` so the frontend can show a hint.
fn copy_internet_url(app: &AppHandle) {
    let url = app
        .try_state::<crate::tunnel::TunnelState>()
        .and_then(|state| {
            state
                .0
                .lock()
                .ok()
                .and_then(|guard| guard.as_ref().map(|info| info.public_url.clone()))
        });

    let _ = tauri::Emitter::emit(app, "tray://copy-internet-url", url);
}

async fn copy_lan_url(app: &AppHandle) {
    let info = network::fetch_network_info(app).await;
    if let Some(url) = info.and_then(|i| i.lan_url) {
        // The opener plugin doesn't do clipboard; use the JS side via an event so
        // the frontend (which has clipboard permission) performs the copy, or fall
        // back to the tauri clipboard plugin if present. We emit an event the app
        // listens for; the app is guaranteed to be loaded when the tray is usable.
        let _ = tauri::Emitter::emit(app, "tray://copy-lan-url", url);
    }
}

async fn show_qr_window(app: &AppHandle) {
    // Reuse an existing QR window if it's already open.
    if let Some(existing) = app.get_webview_window("qr") {
        let _ = existing.set_focus();
        return;
    }

    let lan_url = network::fetch_network_info(app)
        .await
        .and_then(|i| i.lan_url)
        .unwrap_or_else(|| "http://localhost:5000".to_string());

    // The QR window loads a bundled asset that renders the QR client-side using
    // the same `qrcode` approach the in-app LanJoinScreen uses. The URL to encode
    // is passed as a query parameter.
    let encoded = urlencoding_encode(&lan_url);
    let url = format!("desktop/qr.html?url={encoded}");

    let _ = WebviewWindowBuilder::new(app, "qr", WebviewUrl::App(url.into()))
        .title("Aircane — LAN Join QR")
        .inner_size(320.0, 400.0)
        .resizable(false)
        .center()
        .build();
}

/// Minimal percent-encoding for the query value (avoids pulling in an extra crate
/// for a single, well-known input).
fn urlencoding_encode(input: &str) -> String {
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

/**
 * Ambient global declarations used across the app.
 *
 * - `__aircaneErrorBuffer` is a small FIFO buffer of recent console/error strings, installed
 *   in `main.ts` before the app is created so init-time errors are captured. The in-app
 *   feedback feature reads it when a bug report is composed.
 * - `__TAURI__` is injected by the Tauri desktop wrapper when `withGlobalTauri` is enabled.
 */
export {}

declare global {
  interface Window {
    __aircaneErrorBuffer?: string[]
    __TAURI__?: unknown
  }
}

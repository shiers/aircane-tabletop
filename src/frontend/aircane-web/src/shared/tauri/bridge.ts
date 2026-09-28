/**
 * Tauri bridge.
 *
 * All access to Tauri APIs goes through this module so the rest of the Vue app
 * stays browser-agnostic. Every function is a no-op (or returns a safe default)
 * when the app is running in a plain browser rather than inside the desktop
 * wrapper. Detection is via the global `window.__TAURI__`, which Tauri injects
 * when `withGlobalTauri` is enabled in `tauri.conf.json`.
 *
 * We deliberately avoid importing `@tauri-apps/api` at module scope so the plain
 * web build never pulls the dependency into its bundle. Instead we use the
 * globals Tauri exposes (`window.__TAURI__.core.invoke`, `.event.listen`).
 */

type UnlistenFn = () => void

interface TauriGlobal {
  core: {
    invoke: <T = unknown>(cmd: string, args?: Record<string, unknown>) => Promise<T>
  }
  event: {
    listen: <T = unknown>(
      event: string,
      handler: (event: { payload: T }) => void,
    ) => Promise<UnlistenFn>
  }
}

function getTauri(): TauriGlobal | null {
  if (typeof window === 'undefined') return null
  const t = (window as unknown as { __TAURI__?: TauriGlobal }).__TAURI__
  return t ?? null
}

/** True when running inside the Tauri desktop wrapper. */
export function isDesktop(): boolean {
  return getTauri() !== null
}

/**
 * Invoke a Rust command. Resolves to null (and never throws) when not in Tauri,
 * so callers can use it unconditionally.
 */
export async function invoke<T = unknown>(
  cmd: string,
  args?: Record<string, unknown>,
): Promise<T | null> {
  const tauri = getTauri()
  if (!tauri) return null
  try {
    return await tauri.core.invoke<T>(cmd, args)
  } catch (err) {
    console.warn(`[tauri] invoke "${cmd}" failed`, err)
    return null
  }
}

/**
 * Listen for a Tauri event. Returns an unlisten function; when not in Tauri it
 * returns a no-op unlisten so `onUnmounted(unlisten)` is always safe.
 */
export async function listen<T = unknown>(
  event: string,
  handler: (payload: T) => void,
): Promise<UnlistenFn> {
  const tauri = getTauri()
  if (!tauri) return () => {}
  try {
    return await tauri.event.listen<T>(event, (e) => handler(e.payload))
  } catch (err) {
    console.warn(`[tauri] listen "${event}" failed`, err)
    return () => {}
  }
}

// ── Convenience wrappers around the wrapper's Rust commands ────────────────────

/** Open a URL in the system browser (not the webview). */
export function openExternal(url: string): Promise<unknown | null> {
  return invoke('open_external', { url })
}

/** Ask the wrapper to navigate to the in-app AI provider settings. */
export function openSettings(): Promise<unknown | null> {
  return invoke('open_settings')
}

/** Get the backend base URL the sidecar is serving on. */
export function getBaseUrl(): Promise<string | null> {
  return invoke<string>('get_base_url')
}

// ── Event name constants (must match the Rust side) ────────────────────────────

export const TauriEvents = {
  OllamaNotRunning: 'ollama://not-running',
  CopyLanUrl: 'tray://copy-lan-url',
  NavigateAiSettings: 'navigate://ai-settings',
} as const

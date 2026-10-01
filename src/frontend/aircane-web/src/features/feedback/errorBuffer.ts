/**
 * Console-error capture for the in-app feedback feature.
 *
 * `installErrorCapture()` is called from `main.ts` BEFORE `createApp()` so errors thrown during
 * app initialization are caught. It:
 *  - initializes the module buffer `window.__aircaneErrorBuffer` (FIFO, capped at CAP),
 *  - wraps `console.error` to push a formatted string, then calls the original,
 *  - listens for `unhandledrejection` and pushes the reason.
 *
 * The Vue `app.config.errorHandler` is wired separately in `main.ts` (after `createApp`) to push
 * into the same buffer via `pushError`.
 *
 * The buffer only ever holds error strings — never campaign content, character data, or PDFs.
 */

const CAP = 20

function getBuffer(): string[] {
  if (!window.__aircaneErrorBuffer) {
    window.__aircaneErrorBuffer = []
  }
  return window.__aircaneErrorBuffer
}

function stringify(value: unknown): string {
  if (value instanceof Error) {
    return value.stack && value.stack.length > 0 ? value.stack : `${value.name}: ${value.message}`
  }
  if (typeof value === 'string') return value
  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}

/** Push a single error entry into the buffer, evicting the oldest beyond CAP. */
export function pushError(...parts: unknown[]): void {
  const buffer = getBuffer()
  const text = parts.map(stringify).join(' ')
  buffer.push(text)
  while (buffer.length > CAP) {
    buffer.shift()
  }
}

let installed = false

/**
 * Installs console.error and unhandledrejection capture. Idempotent and safe to call once at
 * the very start of app bootstrap (before createApp).
 */
export function installErrorCapture(): void {
  if (installed || typeof window === 'undefined') return
  installed = true

  getBuffer()

  const originalConsoleError = console.error.bind(console)
  console.error = (...args: unknown[]) => {
    try {
      pushError(...args)
    } catch {
      // Never let capture break logging.
    }
    originalConsoleError(...args)
  }

  window.addEventListener('unhandledrejection', (event) => {
    try {
      pushError(event.reason)
    } catch {
      // ignore
    }
  })
}

/**
 * Returns the most recent console errors, each trimmed to its first 3 lines (to keep stacks
 * compact), capped at `limit` (default 5).
 */
export function getConsoleErrors(limit = 5): string[] {
  const buffer = window.__aircaneErrorBuffer ?? []
  return buffer
    .slice(-limit)
    .map((entry) => entry.split('\n').slice(0, 3).join('\n'))
}

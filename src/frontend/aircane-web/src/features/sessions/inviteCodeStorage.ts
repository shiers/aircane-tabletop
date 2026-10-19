/**
 * Keeps a play session's invite code across a page reload or a restart of the app.
 *
 * The server stores only a hash of the invite code and returns the plain text exactly once, from
 * POST /api/campaigns/{id}/start-session. If the host loses it there is no way to show it again,
 * and players who join late cannot be told it. So the host's browser keeps a copy.
 *
 * Stored in **localStorage**, not sessionStorage: a reload keeps either, but sessionStorage dies
 * with the tab or window, and a host who closes the tab (or restarts the desktop app) mid-evening
 * would lose the only copy. The cost is that the code stays on disk in the browser profile, so it
 * is scoped and short-lived:
 *   - one key per play session (`aircane.inviteCode.<sessionId>`), holding the code and when it was saved;
 *   - removed when the host ends the session (see `clearInviteCode`);
 *   - ignored and removed once older than {@link MAX_AGE_MS}, and swept on every save.
 *
 * It is only useful together with the session id, and only while the session is open and the
 * host approves who joins, so a stale entry is low risk. Storage can be unavailable (private mode,
 * blocked site data), so every access is guarded and the app works without it.
 */

const KEY_PREFIX = 'aircane.inviteCode.'

/** Three days: longer than any evening's play, shorter than forever. */
export const MAX_AGE_MS = 3 * 24 * 60 * 60 * 1000

interface StoredInvite {
  code: string
  savedAt: number
}

function storage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

function keyFor(sessionId: string): string {
  return `${KEY_PREFIX}${sessionId}`
}

/** Saves the invite code for a session, and removes any expired codes. */
export function saveInviteCode(sessionId: string, code: string, now: number = Date.now()): void {
  const store = storage()
  if (!store || !sessionId || !code) return
  try {
    sweepExpiredInviteCodes(now)
    const value: StoredInvite = { code, savedAt: now }
    store.setItem(keyFor(sessionId), JSON.stringify(value))
  } catch {
    // Storage full or blocked: the code is still shown this once.
  }
}

/** The saved invite code for a session, or null when there is none or it has expired. */
export function loadInviteCode(sessionId: string, now: number = Date.now()): string | null {
  const store = storage()
  if (!store || !sessionId) return null
  try {
    const raw = store.getItem(keyFor(sessionId))
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<StoredInvite>
    if (typeof parsed.code !== 'string' || typeof parsed.savedAt !== 'number' || now - parsed.savedAt > MAX_AGE_MS) {
      store.removeItem(keyFor(sessionId))
      return null
    }
    return parsed.code
  } catch {
    return null
  }
}

/** Forgets a session's invite code (the session has ended). */
export function clearInviteCode(sessionId: string): void {
  try {
    storage()?.removeItem(keyFor(sessionId))
  } catch {
    // Nothing to do.
  }
}

/** Removes every saved invite code older than {@link MAX_AGE_MS}, including unreadable entries. */
export function sweepExpiredInviteCodes(now: number = Date.now()): void {
  const store = storage()
  if (!store) return
  try {
    const stale: string[] = []
    for (let i = 0; i < store.length; i++) {
      const key = store.key(i)
      if (!key?.startsWith(KEY_PREFIX)) continue
      try {
        const parsed = JSON.parse(store.getItem(key) ?? '') as Partial<StoredInvite>
        if (typeof parsed.savedAt !== 'number' || now - parsed.savedAt > MAX_AGE_MS) stale.push(key)
      } catch {
        stale.push(key)
      }
    }
    stale.forEach((key) => store.removeItem(key))
  } catch {
    // Best effort.
  }
}

/**
 * The rejoin secrets this browser holds: one per session it joined as a player, so a closed tab (or the join
 * link opened in a new tab) can take the same seat back instead of joining as someone new.
 *
 * **Where: `localStorage`**, under `aircane_rejoin_secrets`, as `{ [sessionId]: { secret, displayName, savedAt } }`.
 * Unlike the bearer token (sessionStorage, one tab; see participantSession.ts) a secret has to outlive the tab,
 * and it is safe to share between tabs: it is only ever sent to the rejoin endpoints, never attached to other
 * requests, so a host tab in the same browser is not treated as a player.
 *
 * A secret is 128 random bits, base64url. The server keeps only its hash. At most {@link MAX_REJOIN_SECRETS} are
 * kept (the most recent), which is also how many the sessions page checks when it opens: each check is a request
 * against a rate limit of 30 a minute, so ten leaves room for a few reloads and a click.
 *
 * Storage can be unavailable (private mode, blocked site data): then there is simply nothing to rejoin with,
 * and joining works as before.
 */

const KEY = 'aircane_rejoin_secrets'

/** The most sessions remembered, and so the most "Rejoin" cards (see the file comment). */
export const MAX_REJOIN_SECRETS = 10

export interface RejoinEntry {
  sessionId: string
  secret: string
  /** The name this browser joined with, shown on the card when the host cannot be asked. */
  displayName: string
  /** When the secret was made (ms since the epoch); the newest are kept and listed first. */
  savedAt: number
}

type Stored = Record<string, Omit<RejoinEntry, 'sessionId'>>

function storage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage
  } catch {
    return null
  }
}

function read(): Stored {
  try {
    const raw = storage()?.getItem(KEY)
    if (!raw) return {}
    const parsed = JSON.parse(raw) as unknown
    if (!parsed || typeof parsed !== 'object') return {}
    const result: Stored = {}
    for (const [sessionId, value] of Object.entries(parsed as Record<string, unknown>)) {
      const entry = value as Partial<RejoinEntry> | null
      if (entry && typeof entry.secret === 'string' && entry.secret) {
        result[sessionId] = {
          secret: entry.secret,
          displayName: typeof entry.displayName === 'string' ? entry.displayName : '',
          savedAt: typeof entry.savedAt === 'number' ? entry.savedAt : 0,
        }
      }
    }
    return result
  } catch {
    return {}
  }
}

function write(entries: Stored): void {
  const store = storage()
  if (!store) return
  try {
    // Keep only the newest few, so the list (and the checks the sessions page makes) stays small.
    const kept = Object.entries(entries)
      .sort(([, a], [, b]) => b.savedAt - a.savedAt)
      .slice(0, MAX_REJOIN_SECRETS)
    if (kept.length === 0) store.removeItem(KEY)
    else store.setItem(KEY, JSON.stringify(Object.fromEntries(kept)))
  } catch {
    // Storage full or blocked: this browser just cannot come back without joining again.
  }
}

/** 128 random bits, base64url without padding. */
export function newRejoinSecret(): string {
  const bytes = new Uint8Array(16)
  crypto.getRandomValues(bytes)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/** This browser's secret for a session, or null. */
export function getRejoinSecret(sessionId: string): string | null {
  return read()[sessionId]?.secret ?? null
}

/**
 * The secret to send with a join request: the one this browser already has for the session, or a new one,
 * which is remembered now (so it is the same if the request has to be sent again).
 */
export function getOrCreateRejoinSecret(sessionId: string, displayName: string, now: number = Date.now()): string {
  const entries = read()
  const existing = entries[sessionId]
  if (existing) return existing.secret
  const secret = newRejoinSecret()
  entries[sessionId] = { secret, displayName, savedAt: now }
  write(entries)
  return secret
}

/** Records the name the server accepted (or returned on a rejoin) next to the secret. */
export function rememberRejoinName(sessionId: string, displayName: string): void {
  const entries = read()
  const entry = entries[sessionId]
  if (!entry || entry.displayName === displayName) return
  entries[sessionId] = { ...entry, displayName }
  write(entries)
}

/** Forgets the secret for a session: it has ended, the host declined, or the server no longer knows it. */
export function forgetRejoinSecret(sessionId: string): void {
  const entries = read()
  if (!(sessionId in entries)) return
  delete entries[sessionId]
  write(entries)
}

/** Every remembered session, newest first, at most {@link MAX_REJOIN_SECRETS}. */
export function listRejoinSecrets(): RejoinEntry[] {
  return Object.entries(read())
    .map(([sessionId, entry]) => ({ sessionId, ...entry }))
    .sort((a, b) => b.savedAt - a.savedAt)
    .slice(0, MAX_REJOIN_SECRETS)
}

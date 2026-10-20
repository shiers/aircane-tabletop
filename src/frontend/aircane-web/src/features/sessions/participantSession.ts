/**
 * Remembers who a player is at a table across a page reload or a dropped connection.
 *
 * After POST /api/sessions/{id}/join the server returns a participant token. Everything the player
 * does afterwards (API calls, the SignalR hub) sends it, and the approval, the character and the
 * turn all belong to that participant. If the page forgot it, a reload would mean joining again as
 * a new participant that the host has to approve again.
 *
 * **Where it is kept: `sessionStorage`**, under these keys:
 *   - `participant_token`      the bearer token (the key the API client and the hubs already read)
 *   - `participant_session_id` the play session the token was issued for
 *   - `participant_id`         the participant's id (the hub and the turn order identify players by it)
 *   - `participant_name`       the display name
 *
 * sessionStorage survives a reload and a brief disconnect (a phone that sleeps, a wifi drop), is
 * private to the tab, and is gone when the tab is closed. That scoping is deliberate: the API client
 * attaches the token to every request, so a token kept somewhere shared (localStorage) would also be
 * attached in a host's own tab in the same browser, and the host would be treated as that player.
 * The cost is that closing the tab means joining again (a new request for the host to approve). If
 * that turns out to matter, this file is the only place to change.
 *
 * Storage can be unavailable (private mode, blocked site data), so every access is guarded: without
 * it the app still works, it just cannot resume.
 */

const TOKEN_KEY = 'participant_token'
const SESSION_KEY = 'participant_session_id'
const ID_KEY = 'participant_id'
const NAME_KEY = 'participant_name'

export interface ParticipantSession {
  sessionId: string
  participantId: string
  displayName: string
  token: string
}

function storage(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null
  }
}

/** Keeps a freshly joined participant, replacing whoever was remembered before. */
export function saveParticipantSession(session: ParticipantSession): void {
  const store = storage()
  if (!store) return
  try {
    store.setItem(TOKEN_KEY, session.token)
    store.setItem(SESSION_KEY, session.sessionId)
    store.setItem(ID_KEY, session.participantId)
    store.setItem(NAME_KEY, session.displayName)
  } catch {
    // Storage full or blocked: this page still works, it just cannot be resumed.
  }
}

/** The remembered participant, or null when there is none (or part of it is missing). */
export function loadParticipantSession(): ParticipantSession | null {
  const store = storage()
  if (!store) return null
  try {
    const token = store.getItem(TOKEN_KEY)
    const sessionId = store.getItem(SESSION_KEY)
    const participantId = store.getItem(ID_KEY)
    if (!token || !sessionId || !participantId) return null
    return { token, sessionId, participantId, displayName: store.getItem(NAME_KEY) ?? 'Player' }
  } catch {
    return null
  }
}

/**
 * The remembered participant, whichever session it is for, unless its token has expired (then it is removed).
 * For screens that are not about one session, such as the sidebar and the join index: "is this browser a player
 * at some table?". Whether the server still accepts the token is for the screen that goes there to find out.
 */
export function loadLiveParticipantSession(now: number = Date.now()): ParticipantSession | null {
  const saved = loadParticipantSession()
  if (!saved) return null
  if (isTokenExpired(saved.token, now)) {
    clearParticipantSession()
    return null
  }
  return saved
}

/**
 * The remembered participant, only if it belongs to this play session and its token has not expired.
 * Anything else is stale: it is removed, so it can never be sent with a later request.
 */
export function loadParticipantSessionFor(sessionId: string, now: number = Date.now()): ParticipantSession | null {
  const saved = loadParticipantSession()
  if (!saved) {
    // A token with no identity next to it (an older build, or a half-written save) cannot be resumed.
    try {
      if (storage()?.getItem(TOKEN_KEY)) clearParticipantSession()
    } catch {
      // Storage is unavailable; there is nothing to clear.
    }
    return null
  }
  if (saved.sessionId !== sessionId || isTokenExpired(saved.token, now)) {
    clearParticipantSession()
    return null
  }
  return saved
}

/** Forgets the participant, and with it the token every later request would carry. */
export function clearParticipantSession(): void {
  const store = storage()
  if (!store) return
  try {
    store.removeItem(TOKEN_KEY)
    store.removeItem(SESSION_KEY)
    store.removeItem(ID_KEY)
    store.removeItem(NAME_KEY)
  } catch {
    // Nothing to do.
  }
}

/**
 * True when the token's `exp` claim has passed. The signature is the server's business: this only
 * spares a round trip for a token that is certainly dead. A token that cannot be read counts as
 * not expired, so the server decides.
 */
export function isTokenExpired(token: string, now: number = Date.now()): boolean {
  try {
    const payload = token.split('.')[1]
    if (!payload) return false
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const exp = (JSON.parse(json) as { exp?: unknown }).exp
    return typeof exp === 'number' && exp * 1000 <= now
  } catch {
    return false
  }
}

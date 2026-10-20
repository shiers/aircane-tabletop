import axios from 'axios'

/**
 * What went wrong with a player-side request, in the player's terms.
 *
 * The server answers with ProblemDetails (`title`, `detail`, and for a denied participant a `code`),
 * a bare 404 for a session that does not exist, a bare 403 for a caller who may not act, and an
 * empty 500 when the AI provider fails. Axios turns all of those into "Request failed with status
 * code NNN", which tells a player nothing, so every player-facing error goes through here.
 */

/** The code the backend puts on the 403 it sends a participant the host denied. */
export const PARTICIPANT_DENIED_CODE = 'participant-denied'

export const DENIED_MESSAGE =
  'The host declined your request to join this session. If you think that was a mistake, ask the host, then join again with the invite code.'

function problemOf(err: unknown): { status: number | null; detail: string | null; code: string | null } {
  if (!axios.isAxiosError(err) || !err.response) return { status: null, detail: null, code: null }
  const data = err.response.data as { detail?: unknown; code?: unknown } | undefined
  return {
    status: err.response.status,
    detail: typeof data?.detail === 'string' && data.detail.trim() ? data.detail.trim() : null,
    code: typeof data?.code === 'string' ? data.code : null,
  }
}

/** True when the failure is the refusal sent to a participant the host denied. */
export function isParticipantDenied(err: unknown): boolean {
  const { status, code } = problemOf(err)
  return status === 403 && code === PARTICIPANT_DENIED_CODE
}

/** True when the server rejected the stored token (expired, revoked, wrong session). */
export function isTokenRejected(err: unknown): boolean {
  return problemOf(err).status === 401
}

/** True when there was no answer at all (host asleep, wrong network, server restarting). */
export function isUnreachable(err: unknown): boolean {
  return axios.isAxiosError(err) && !err.response
}

const UNREACHABLE = "Can't reach the host. Check that you are on the same network as the host and that the host's computer is still running."

/** A failed join, ready to show under the form. */
export function mapJoinError(err: unknown): string {
  if (!axios.isAxiosError(err)) {
    return err instanceof Error && err.message ? err.message : 'Could not join the session.'
  }
  if (!err.response) return UNREACHABLE

  const { status, detail } = problemOf(err)
  switch (status) {
    case 400:
      // "Invalid invite code." and the validation messages are written for people; the "not active"
      // one names the session's GUID, which is not.
      if (detail && /invalid invite code/i.test(detail)) {
        return "That invite code isn't right. Check it with the host and try again."
      }
      if (detail && /is not active/i.test(detail)) {
        return 'This session is not open for joining. It may have ended; ask the host to start a new one.'
      }
      return detail ?? 'The host could not accept that join request. Check your name and the invite code.'
    case 404:
      return 'That session was not found. Check the join link with the host.'
    case 401:
    case 403:
      return detail ?? 'The server did not accept this join request. Reload the page and try again, or ask the host for a fresh join link.'
    case 409:
      // "This browser already has a place in this session..."
      return detail ?? 'This browser already has a place in this session. Go back to it instead of joining again.'
    case 429:
      return 'Too many tries in a short time. Wait a minute, then try again.'
    default:
      return detail ?? `Could not join the session (server error ${status}). Try again in a moment.`
  }
}

/**
 * Why taking a seat back with the rejoin secret did not work, as the screens act on it:
 *  - `gone`: the server has nothing to go back to (unknown secret, ended or unknown session): forget the secret;
 *  - `denied`: the host declined this request: forget the secret, say so;
 *  - `unreachable`: no answer: keep the secret, say the host cannot be reached;
 *  - `busy`: rate limited: keep the secret, try again in a minute;
 *  - `other`: anything else: keep the secret.
 */
export type RejoinFailure = 'gone' | 'denied' | 'unreachable' | 'busy' | 'other'

export function rejoinFailure(err: unknown): RejoinFailure {
  if (isUnreachable(err)) return 'unreachable'
  if (isParticipantDenied(err)) return 'denied'
  const { status } = problemOf(err)
  if (status === 404) return 'gone'
  if (status === 429) return 'busy'
  return 'other'
}

/** A failed rejoin, ready to show on its card. */
export function mapRejoinError(err: unknown): string {
  switch (rejoinFailure(err)) {
    case 'unreachable':
      return UNREACHABLE
    case 'denied':
      return DENIED_MESSAGE
    case 'gone':
      return 'That session has ended, or no longer has a place for this browser. Join with the invite code.'
    case 'busy':
      return 'Too many tries in a short time. Wait a minute, then try again.'
    default:
      return problemOf(err).detail ?? 'Could not go back to that session. Try again in a moment.'
  }
}

/**
 * The server's own words for a failed request (ProblemDetails `detail`, then `title`), or a plain sentence for
 * the status, never axios's "Request failed with status code 401".
 */
export function serverMessage(err: unknown, fallback = 'Something went wrong. Try again in a moment.'): string {
  if (!axios.isAxiosError(err)) return err instanceof Error && err.message ? err.message : fallback
  if (!err.response) return UNREACHABLE
  const { status, detail } = problemOf(err)
  if (detail) return detail
  const title = (err.response.data as { title?: unknown } | undefined)?.title
  if (typeof title === 'string' && title.trim()) return title.trim()
  if (status === 401) return 'Your login for this table is no longer valid. Join again to continue.'
  if (status === 403) return 'You are not allowed to do that here.'
  if (status === 404) return 'That was not found. It may have ended.'
  return fallback
}

/** A failed action in the adventure, ready to show beside the action box. */
export function mapActionError(err: unknown): string {
  if (!axios.isAxiosError(err)) {
    return err instanceof Error && err.message ? err.message : 'Your action could not be sent.'
  }
  if (!err.response) {
    if (err.code === 'ECONNABORTED') {
      return 'The AI DM took too long to answer. Your action may still have gone through; check the story, then try again.'
    }
    return UNREACHABLE
  }

  const { status, detail, code } = problemOf(err)
  if (code === PARTICIPANT_DENIED_CODE) return DENIED_MESSAGE

  switch (status) {
    case 400:
    case 409:
      // "It is not your turn..." and "Adventure session is Completed; it can no longer be played."
      return detail ?? 'The adventure cannot take that action right now.'
    case 403:
      // A turn problem comes with a detail; a bare 403 means the caller is not allowed to act at all.
      return detail ?? 'You are not allowed to act in this adventure. Ask the host to check that you are approved and in the turn order.'
    case 401:
      return 'Your login for this table is no longer valid. Reload the page to rejoin.'
    case 404:
      return 'This adventure is no longer running.'
    case 429:
      return 'Too many requests in a short time. Wait a moment and try again.'
    default:
      if (status !== null && status >= 500) {
        return 'The AI DM could not answer right now (the AI provider failed). Wait a moment and try again; the host can check the AI settings if it keeps happening.'
      }
      return detail ?? 'Your action could not be sent.'
  }
}

/**
 * A failed "End my turn". A 403 is the server saying the turn is not theirs (any more): it names who has it,
 * and covers a request that arrived after the turn had already moved on. The view reads the run again.
 */
export function mapEndTurnError(err: unknown): string {
  if (!axios.isAxiosError(err)) {
    return err instanceof Error && err.message ? err.message : 'Your turn could not be ended.'
  }
  if (!err.response) return UNREACHABLE

  const { status, detail, code } = problemOf(err)
  if (code === PARTICIPANT_DENIED_CODE) return DENIED_MESSAGE

  switch (status) {
    case 403:
      return detail ?? 'It is not your turn any more.'
    case 409:
      return detail ?? 'The turn has already moved on.'
    case 400:
      return detail ?? 'The turn cannot be ended right now.'
    case 401:
      return 'Your login for this table is no longer valid. Reload the page to rejoin.'
    case 404:
      return 'This adventure is no longer running.'
    default:
      return detail ?? 'Your turn could not be ended. Try again in a moment.'
  }
}

/** A failed read of the adventure state; the view keeps the last state it had. */
export function mapRunLoadError(err: unknown): string {
  if (isUnreachable(err)) return UNREACHABLE
  const { status, detail } = problemOf(err)
  if (status === 403) return detail ?? 'You are not allowed to see this adventure yet. The host may not have approved you.'
  if (status === 401) return 'Your login for this table is no longer valid. Reload the page to rejoin.'
  return detail ?? 'Could not load the adventure. Retrying…'
}

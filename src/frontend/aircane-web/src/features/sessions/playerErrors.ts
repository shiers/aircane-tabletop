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
      return 'The server did not accept this join request. Reload the page and try again, or ask the host for a fresh join link.'
    case 429:
      return 'Too many tries in a short time. Wait a minute, then try again.'
    default:
      return detail ?? `Could not join the session (server error ${status}). Try again in a moment.`
  }
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

/** A failed read of the adventure state; the view keeps the last state it had. */
export function mapRunLoadError(err: unknown): string {
  if (isUnreachable(err)) return UNREACHABLE
  const { status, detail } = problemOf(err)
  if (status === 403) return detail ?? 'You are not allowed to see this adventure yet. The host may not have approved you.'
  if (status === 401) return 'Your login for this table is no longer valid. Reload the page to rejoin.'
  return detail ?? 'Could not load the adventure. Retrying…'
}

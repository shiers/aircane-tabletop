import { rejoinSession, type RejoinSessionResult } from './api'
import { saveParticipantSession } from './participantSession'
import { mapRejoinError, rejoinFailure, type RejoinFailure } from './playerErrors'
import { forgetRejoinSecret, getRejoinSecret, rememberRejoinName } from './rejoinSecrets'

/**
 * Taking a seat back with the browser's rejoin secret, shared by the join page (a join link opened with no
 * token in this tab) and the sessions page's "Rejoin" cards.
 *
 * On success the fresh token is kept like a join's (participantSession.ts), so everything after works as if the
 * player had never left. When the server has nothing to go back to, or the host declined the request or removed the
 * player, the secret is
 * forgotten: a stale secret must never stand in the way of joining again. When the host cannot be reached (or the
 * rate limit bit), the secret is kept for the next try.
 */

export type RejoinOutcome =
  | { kind: 'none' }
  | { kind: 'rejoined'; result: RejoinSessionResult }
  | { kind: 'failed'; failure: RejoinFailure; message: string }

/** Keeps a rejoined seat as the tab's login. */
export function adoptRejoinedSeat(sessionId: string, result: RejoinSessionResult): void {
  saveParticipantSession({
    sessionId,
    participantId: result.participantId,
    displayName: result.displayName,
    token: result.participantToken,
  })
  rememberRejoinName(sessionId, result.displayName)
}

/** Tries the rejoin secret this browser holds for the session; `none` when it holds none. */
export async function rejoinWithStoredSecret(
  sessionId: string,
  send: typeof rejoinSession = rejoinSession,
): Promise<RejoinOutcome> {
  const secret = getRejoinSecret(sessionId)
  if (!secret) return { kind: 'none' }
  try {
    const result = await send(sessionId, secret)
    adoptRejoinedSeat(sessionId, result)
    return { kind: 'rejoined', result }
  } catch (err) {
    const failure = rejoinFailure(err)
    if (failure === 'gone' || failure === 'denied' || failure === 'removed') forgetRejoinSecret(sessionId)
    return { kind: 'failed', failure, message: mapRejoinError(err) }
  }
}

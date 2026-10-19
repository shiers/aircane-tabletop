import axios from 'axios'

/**
 * What went wrong with a group-play request, in the host's terms.
 *
 * The backend answers with a bare 403 (no body) when the caller is not the host, and with
 * ProblemDetails `detail` text for 400 and 409. Some of those details name GUIDs or internal
 * rules the host cannot act on, so they are translated here; everything else is passed through.
 */
export type GroupErrorKind =
  | 'not-host'
  | 'conflict'
  | 'bad-request'
  | 'not-found'
  | 'rate-limited'
  | 'unreachable'
  | 'unknown'

export interface GroupError {
  kind: GroupErrorKind
  /** The HTTP status, or null when there was no response. */
  status: number | null
  /** Ready to show to the host. */
  message: string
}

/** The action that failed, for the wording of a few messages. */
export type GroupAction =
  | 'create-session'
  | 'start-group'
  | 'advance-turn'
  | 'advance-scene'
  | 'abandon'
  | 'add-participant'
  | 'load'

const NOT_HOST_MESSAGE =
  'Only the host can do this. Open Aircane on the host computer at http://localhost (not through its ' +
  'network address) and try again. If you are a player, ask the host.'

const ACTION_VERBS: Record<GroupAction, string> = {
  'create-session': 'create the play session',
  'start-group': 'start the adventure',
  'advance-turn': 'pass the turn',
  'advance-scene': 'advance the scene',
  abandon: 'abandon the adventure',
  'add-participant': 'add that player',
  load: 'load the adventure',
}

/** `detail` from a ProblemDetails body, if the response has one. */
function detailOf(data: unknown): string | null {
  if (data && typeof data === 'object' && 'detail' in data) {
    const detail = (data as { detail?: unknown }).detail
    if (typeof detail === 'string' && detail.trim()) return detail.trim()
  }
  return null
}

/** Turns a failed group-play request into a message the host can act on. */
export function mapGroupError(err: unknown, action: GroupAction): GroupError {
  const verb = ACTION_VERBS[action]

  if (!axios.isAxiosError(err)) {
    const message = err instanceof Error && err.message ? err.message : `Could not ${verb}.`
    return { kind: 'unknown', status: null, message }
  }

  const response = err.response
  if (!response) {
    return {
      kind: 'unreachable',
      status: null,
      message: `Can't reach the Aircane server to ${verb}. Check that it is still running.`,
    }
  }

  const status = response.status
  const detail = detailOf(response.data)

  switch (status) {
    case 401:
    case 403:
      return { kind: 'not-host', status, message: NOT_HOST_MESSAGE }

    case 404:
      return {
        kind: 'not-found',
        status,
        message: 'The adventure, campaign or session could not be found. It may have been deleted.',
      }

    case 409:
      return { kind: 'conflict', status, message: detail ?? `Could not ${verb}: it conflicts with what is already running.` }

    case 429:
      return {
        kind: 'rate-limited',
        status,
        message: 'Too many requests in a short time. Wait a minute and try again.',
      }

    case 400: {
      // The backend lists the offending GUIDs; the host cannot do anything with those.
      if (detail?.startsWith('Not active participants of the campaign')) {
        return {
          kind: 'bad-request',
          status,
          message:
            'One or more of the selected players is no longer an approved player in this session. ' +
            'The list has been refreshed; check it and try again.',
        }
      }
      return { kind: 'bad-request', status, message: detail ?? `Could not ${verb}.` }
    }

    default:
      return { kind: 'unknown', status, message: detail ?? `Could not ${verb} (server error ${status}).` }
  }
}

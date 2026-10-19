import { describe, it, expect } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import { mapGroupError } from '../groupErrors'

/** An axios error carrying an HTTP response, as the shared client produces for 4xx/5xx. */
function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

/** An axios error with no response: the server was not reached. */
function networkError(): AxiosError {
  return new AxiosError('Network Error', 'ERR_NETWORK')
}

describe('mapGroupError', () => {
  describe('403 and 401 (not the host)', () => {
    it('explains who can start and where to open the app, for a bare 403', () => {
      const result = mapGroupError(httpError(403), 'start-group')
      expect(result.kind).toBe('not-host')
      expect(result.status).toBe(403)
      expect(result.message).toMatch(/only the host/i)
      expect(result.message).toContain('http://localhost')
    })

    it('says the same for a 401', () => {
      const result = mapGroupError(httpError(401), 'advance-turn')
      expect(result.kind).toBe('not-host')
      expect(result.message).toMatch(/only the host/i)
    })

    it('does not pass a stray body through', () => {
      expect(mapGroupError(httpError(403, { detail: 'internal detail' }), 'start-group').message).not.toContain('internal detail')
    })
  })

  describe('409 (conflict)', () => {
    it('shows the server message for an adventure already in progress', () => {
      const detail = 'This campaign already has an adventure in progress. Abandon or complete it before starting another.'
      const result = mapGroupError(httpError(409, { title: 'Adventure already in progress', detail }), 'start-group')
      expect(result.kind).toBe('conflict')
      expect(result.status).toBe(409)
      expect(result.message).toBe(detail)
    })

    it('shows the solo-run message', () => {
      const detail =
        'This campaign is already running this adventure as a solo run. Abandon or complete it before starting a group run.'
      expect(mapGroupError(httpError(409, { detail }), 'start-group').message).toBe(detail)
    })

    it('shows the already-in-the-adventure message when adding a player', () => {
      const detail = 'That participant is already in this adventure run.'
      expect(mapGroupError(httpError(409, { detail }), 'add-participant').message).toBe(detail)
    })

    it('has a fallback when there is no detail', () => {
      const result = mapGroupError(httpError(409), 'start-group')
      expect(result.kind).toBe('conflict')
      expect(result.message).toMatch(/start the adventure/i)
    })
  })

  describe('400 (bad request)', () => {
    it('shows the server message', () => {
      const detail = 'This campaign has no play session in progress. Start a session and let the players join before starting a group run.'
      const result = mapGroupError(httpError(400, { detail }), 'start-group')
      expect(result.kind).toBe('bad-request')
      expect(result.message).toBe(detail)
    })

    it('replaces the message that lists GUIDs with one the host can act on', () => {
      const detail =
        'Not active participants of the campaign\'s current session: 3f2504e0-4f89-11d3-9a0c-0305e82c3301, 9b2e8a1c-0000-4000-8000-000000000002.'
      const result = mapGroupError(httpError(400, { detail }), 'start-group')
      expect(result.kind).toBe('bad-request')
      expect(result.message).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/)
      expect(result.message).toMatch(/no longer an approved player/i)
    })

    it('falls back to a plain message', () => {
      expect(mapGroupError(httpError(400), 'advance-scene').message).toBe('Could not advance the scene.')
    })
  })

  describe('other statuses', () => {
    it('explains a 404', () => {
      const result = mapGroupError(httpError(404, { detail: "Adventure 'x' not found." }), 'start-group')
      expect(result.kind).toBe('not-found')
      expect(result.message).toMatch(/could not be found/i)
    })

    it('explains a 429', () => {
      expect(mapGroupError(httpError(429), 'create-session').kind).toBe('rate-limited')
    })

    it('reports a server error with its code and any detail', () => {
      const result = mapGroupError(httpError(500), 'abandon')
      expect(result.kind).toBe('unknown')
      expect(result.message).toContain('500')
      expect(mapGroupError(httpError(500, { detail: 'boom' }), 'abandon').message).toBe('boom')
    })
  })

  describe('no response', () => {
    it('says the server could not be reached, naming the action', () => {
      const result = mapGroupError(networkError(), 'advance-turn')
      expect(result.kind).toBe('unreachable')
      expect(result.status).toBeNull()
      expect(result.message).toMatch(/can't reach the aircane server to pass the turn/i)
    })
  })

  describe('errors that are not from axios', () => {
    it('uses the message of a plain Error', () => {
      expect(mapGroupError(new Error('boom'), 'load').message).toBe('boom')
    })

    it('has a fallback for anything else', () => {
      const result = mapGroupError('nope', 'load')
      expect(result.kind).toBe('unknown')
      expect(result.message).toBe('Could not load the adventure.')
    })
  })

  describe('wording by action', () => {
    it.each([
      ['create-session', 'create the play session'],
      ['start-group', 'start the adventure'],
      ['advance-turn', 'pass the turn'],
      ['advance-scene', 'advance the scene'],
      ['abandon', 'abandon the adventure'],
      ['add-participant', 'add that player'],
    ] as const)('names %s', (action, phrase) => {
      expect(mapGroupError(httpError(400), action).message).toBe(`Could not ${phrase}.`)
    })
  })
})

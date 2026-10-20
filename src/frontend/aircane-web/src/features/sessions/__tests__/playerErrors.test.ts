import { describe, it, expect } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import {
  DENIED_MESSAGE,
  PARTICIPANT_DENIED_CODE,
  isParticipantDenied,
  isTokenRejected,
  isUnreachable,
  mapActionError,
  mapEndTurnError,
  mapJoinError,
  mapRunLoadError,
} from '../playerErrors'

/** What axios throws for a response with this status and body. */
function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

/** What axios throws when nothing answered. */
function networkError(code = 'ERR_NETWORK'): AxiosError {
  return new AxiosError('Network Error', code)
}

describe('mapJoinError', () => {
  it('says the invite code is wrong, in words, for the server\'s "Invalid invite code."', () => {
    const message = mapJoinError(httpError(400, { title: 'Invalid invite code', detail: 'Invalid invite code.' }))

    expect(message).toContain("invite code isn't right")
    expect(message).not.toContain('status code')
  })

  it('says the session is not open, without the session GUID, when it has ended', () => {
    const message = mapJoinError(
      httpError(400, { detail: "Session '0d5c4f0e-1111-2222-3333-444455556666' is not active." }),
    )

    expect(message).toContain('not open for joining')
    expect(message).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/)
  })

  it('says the session was not found for a bare 404', () => {
    expect(mapJoinError(httpError(404))).toContain('session was not found')
  })

  it('passes a validation message through', () => {
    expect(mapJoinError(httpError(400, { detail: 'Display name is required.' }))).toBe('Display name is required.')
  })

  it('never shows "Request failed with status code 401"', () => {
    const message = mapJoinError(httpError(401))

    expect(message).not.toContain('Request failed')
    expect(message).toContain('did not accept')
  })

  it('explains a rate limit', () => {
    expect(mapJoinError(httpError(429))).toContain('Too many tries')
  })

  it('says the host cannot be reached when nothing answered', () => {
    expect(mapJoinError(networkError())).toContain("Can't reach the host")
  })
})

describe('mapActionError', () => {
  it('shows the server\'s turn message for a 403 that has one', () => {
    const message = mapActionError(httpError(403, { title: 'Not your turn', detail: 'It is not your turn. Waiting for Bob.' }))

    expect(message).toBe('It is not your turn. Waiting for Bob.')
  })

  it('explains a bare 403', () => {
    expect(mapActionError(httpError(403))).toContain('not allowed to act')
  })

  it('shows the server\'s detail for a 409', () => {
    expect(mapActionError(httpError(409, { detail: 'That conflicts with the running adventure.' }))).toBe(
      'That conflicts with the running adventure.',
    )
  })

  it('shows the server\'s detail for a 400 about a finished adventure', () => {
    expect(
      mapActionError(httpError(400, { detail: 'Adventure session is Completed; it can no longer be played.' })),
    ).toContain('can no longer be played')
  })

  it('calls an empty 500 an AI provider problem and says what to do', () => {
    const message = mapActionError(httpError(500))

    expect(message).toContain('AI provider failed')
    expect(message).not.toContain('status code')
  })

  it('explains a timeout', () => {
    expect(mapActionError(networkError('ECONNABORTED'))).toContain('took too long')
  })

  it('says the adventure is gone for a 404', () => {
    expect(mapActionError(httpError(404))).toContain('no longer running')
  })

  it('shows the declined message if the host denied this participant meanwhile', () => {
    expect(mapActionError(httpError(403, { code: PARTICIPANT_DENIED_CODE }))).toBe(DENIED_MESSAGE)
  })
})

describe('error classification', () => {
  it('recognises the denial the server sends to a participant the host declined', () => {
    expect(isParticipantDenied(httpError(403, { code: 'participant-denied' }))).toBe(true)
    expect(isParticipantDenied(httpError(403, { title: 'Not your turn' }))).toBe(false)
    expect(isParticipantDenied(httpError(401))).toBe(false)
  })

  it('recognises a rejected token and an unreachable host', () => {
    expect(isTokenRejected(httpError(401))).toBe(true)
    expect(isTokenRejected(httpError(403))).toBe(false)
    expect(isUnreachable(networkError())).toBe(true)
    expect(isUnreachable(httpError(500))).toBe(false)
  })
})

describe('mapRunLoadError', () => {
  it('explains a 403 as not being let in yet', () => {
    expect(mapRunLoadError(httpError(403))).toContain('not allowed to see')
  })

  it('keeps the host-unreachable wording', () => {
    expect(mapRunLoadError(networkError())).toContain("Can't reach the host")
  })
})

describe('mapEndTurnError', () => {
  it("shows the server's message for a 403 (not your turn, or the turn already moved on)", () => {
    expect(mapEndTurnError(httpError(403, { detail: 'It is not your turn. Waiting for Bob.' }))).toBe('It is not your turn. Waiting for Bob.')
  })

  it('explains a bare 403', () => {
    expect(mapEndTurnError(httpError(403))).toContain('not your turn')
  })

  it('shows the declined message if the host denied this participant meanwhile', () => {
    expect(mapEndTurnError(httpError(403, { code: PARTICIPANT_DENIED_CODE }))).toBe(DENIED_MESSAGE)
  })

  it("shows the server's detail for a 409 and a 400", () => {
    expect(mapEndTurnError(httpError(409, { detail: 'The turn has already passed to Bob.' }))).toBe('The turn has already passed to Bob.')
    expect(mapEndTurnError(httpError(400, { detail: 'This adventure run is not a group run.' }))).toContain('not a group run')
  })

  it('says the host cannot be reached, and what to do about an expired login', () => {
    expect(mapEndTurnError(networkError())).toContain("Can't reach the host")
    expect(mapEndTurnError(httpError(401))).toContain('Reload the page')
  })

  it("never shows axios's status-code text", () => {
    expect(mapEndTurnError(httpError(500))).not.toContain('status code')
    expect(mapEndTurnError(httpError(404))).toContain('no longer running')
  })
})

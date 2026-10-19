import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  saveParticipantSession,
  loadParticipantSession,
  loadParticipantSessionFor,
  clearParticipantSession,
  isTokenExpired,
  type ParticipantSession,
} from '../participantSession'

/** A JWT-shaped string whose payload carries `exp` (seconds). The signature is not checked client-side. */
function tokenExpiring(expSeconds: number): string {
  const payload = btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
  return `header.${payload}.signature`
}

const NOW = Date.UTC(2026, 9, 20, 12, 0, 0)
const FUTURE = tokenExpiring(NOW / 1000 + 3600)
const PAST = tokenExpiring(NOW / 1000 - 60)

const saved: ParticipantSession = {
  sessionId: 'session-1',
  participantId: 'participant-1',
  displayName: 'Thorin',
  token: FUTURE,
}

describe('participantSession', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.restoreAllMocks()
  })

  it('remembers who joined, in sessionStorage, under the keys the API client and hubs already read', () => {
    saveParticipantSession(saved)

    expect(sessionStorage.getItem('participant_token')).toBe(FUTURE)
    expect(sessionStorage.getItem('participant_session_id')).toBe('session-1')
    expect(sessionStorage.getItem('participant_id')).toBe('participant-1')
    expect(sessionStorage.getItem('participant_name')).toBe('Thorin')
    expect(loadParticipantSession()).toEqual(saved)
  })

  it('does not use localStorage, so a host tab in the same browser never inherits the token', () => {
    saveParticipantSession(saved)

    expect(localStorage.getItem('participant_token')).toBeNull()
  })

  it('returns the participant for the session it was issued for', () => {
    saveParticipantSession(saved)

    expect(loadParticipantSessionFor('session-1', NOW)).toEqual(saved)
  })

  it('drops a login that belongs to another session, so it can never ride along with a fresh join', () => {
    saveParticipantSession(saved)

    expect(loadParticipantSessionFor('session-2', NOW)).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
  })

  it('drops a login whose token has expired', () => {
    saveParticipantSession({ ...saved, token: PAST })

    expect(loadParticipantSessionFor('session-1', NOW)).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
  })

  it('drops a lone token with no identity next to it (a half-written save)', () => {
    sessionStorage.setItem('participant_token', FUTURE)

    expect(loadParticipantSessionFor('session-1', NOW)).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
  })

  it('clear removes everything', () => {
    saveParticipantSession(saved)

    clearParticipantSession()

    expect(loadParticipantSession()).toBeNull()
    expect(sessionStorage.length).toBe(0)
  })

  it('treats a token it cannot read as not expired, and leaves the decision to the server', () => {
    expect(isTokenExpired('not-a-jwt', NOW)).toBe(false)
    expect(isTokenExpired('a.%%%.c', NOW)).toBe(false)
    expect(isTokenExpired(FUTURE, NOW)).toBe(false)
    expect(isTokenExpired(PAST, NOW)).toBe(true)
  })

  it('works, without remembering, when storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => saveParticipantSession(saved)).not.toThrow()
    expect(loadParticipantSession()).toBeNull()
    expect(loadParticipantSessionFor('session-1', NOW)).toBeNull()
    expect(() => clearParticipantSession()).not.toThrow()
  })
})

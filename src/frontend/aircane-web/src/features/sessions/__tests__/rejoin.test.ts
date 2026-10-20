import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import * as sessionsApi from '../api'
import { rejoinWithStoredSecret } from '../rejoin'
import { getOrCreateRejoinSecret, getRejoinSecret } from '../rejoinSecrets'
import { loadParticipantSession } from '../participantSession'
import { serverMessage } from '../playerErrors'
import { useSessionStore } from '../store'

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return { ...actual, rejoinSession: vi.fn(), joinSession: vi.fn() }
})

const mockedRejoin = vi.mocked(sessionsApi.rejoinSession)
const mockedJoin = vi.mocked(sessionsApi.joinSession)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}
const networkError = () => new AxiosError('Network Error', 'ERR_NETWORK')
const denied = () => httpError(403, { title: 'Join request declined', code: 'participant-denied' })

const seat = (isApproved: boolean): sessionsApi.RejoinSessionResult => ({
  participantId: 'participant-1',
  displayName: 'Bifur',
  role: sessionsApi.ParticipantRole.Player,
  isApproved,
  status: isApproved ? 'approved' : 'pending',
  characterId: 'character-1',
  sessionName: 'Friday Table',
  participantToken: 'fresh-token',
})

describe('rejoinWithStoredSecret', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  it('does nothing when the browser holds no secret for the session', async () => {
    expect(await rejoinWithStoredSecret('session-1')).toEqual({ kind: 'none' })
    expect(mockedRejoin).not.toHaveBeenCalled()
  })

  it('takes the seat back and keeps the fresh token as the tab login', async () => {
    const secret = getOrCreateRejoinSecret('session-1', 'Bifur')
    mockedRejoin.mockResolvedValue(seat(true))

    const outcome = await rejoinWithStoredSecret('session-1')

    expect(mockedRejoin).toHaveBeenCalledWith('session-1', secret)
    expect(outcome).toMatchObject({ kind: 'rejoined', result: { participantId: 'participant-1', isApproved: true } })
    expect(loadParticipantSession()).toEqual({
      sessionId: 'session-1',
      participantId: 'participant-1',
      displayName: 'Bifur',
      token: 'fresh-token',
    })
    expect(getRejoinSecret('session-1')).toBe(secret)   // kept for next time
  })

  it.each([
    ['nothing to go back to (404)', () => httpError(404, { title: 'No place to return to' }), 'gone'],
    ['a declined request (403 participant-denied)', denied, 'denied'],
  ])('forgets the secret after %s, so it never blocks a fresh join', async (_label, error, failure) => {
    getOrCreateRejoinSecret('session-1', 'Bifur')
    mockedRejoin.mockRejectedValue(error())

    const outcome = await rejoinWithStoredSecret('session-1')

    expect(outcome).toMatchObject({ kind: 'failed', failure })
    expect(getRejoinSecret('session-1')).toBeNull()
    expect(loadParticipantSession()).toBeNull()
  })

  it.each([
    ['the host cannot be reached', networkError, 'unreachable'],
    ['the rate limit bit', () => httpError(429), 'busy'],
  ])('keeps the secret when %s', async (_label, error, failure) => {
    const secret = getOrCreateRejoinSecret('session-1', 'Bifur')
    mockedRejoin.mockRejectedValue(error())

    const outcome = await rejoinWithStoredSecret('session-1')

    expect(outcome).toMatchObject({ kind: 'failed', failure })
    expect(getRejoinSecret('session-1')).toBe(secret)
  })
})

describe('joining from a browser that already has the seat', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  it('takes the seat back when the server says this browser is already in the session (409)', async () => {
    const secret = getOrCreateRejoinSecret('session-1', 'Bifur')
    mockedJoin.mockRejectedValue(httpError(409, { detail: 'This browser already has a place in this session.' }))
    mockedRejoin.mockResolvedValue(seat(false))
    const store = useSessionStore()

    const result = await store.joinSession('session-1', { displayName: 'Bifur', inviteCode: 'ABC123' })

    expect(mockedJoin).toHaveBeenCalledWith('session-1', expect.objectContaining({ rejoinSecret: secret }))
    expect(mockedRejoin).toHaveBeenCalledWith('session-1', secret)
    expect(result).toMatchObject({ participantId: 'participant-1', isApproved: false, participantToken: 'fresh-token' })
    expect(store.error).toBeNull()
  })

  it('makes no secret for a join with no session in it', async () => {
    mockedJoin.mockResolvedValue({ participantId: 'p', displayName: 'Bifur', role: sessionsApi.ParticipantRole.Player, isApproved: false, participantToken: 't' })

    await useSessionStore().joinSession('', { displayName: 'Bifur', inviteCode: 'ABC123' })

    expect(mockedJoin).toHaveBeenCalledWith('', expect.objectContaining({ rejoinSecret: undefined }))
    expect(localStorage.getItem('aircane_rejoin_secrets')).toBeNull()
  })
})

describe('serverMessage', () => {
  it('uses the server\'s words, never "Request failed with status code 401"', () => {
    expect(serverMessage(httpError(401))).toBe('Your login for this table is no longer valid. Join again to continue.')
    expect(serverMessage(httpError(409, { detail: 'That participant is already approved.' }))).toBe('That participant is already approved.')
    expect(serverMessage(httpError(403, { title: 'Waiting for approval' }))).toBe('Waiting for approval')
    expect(serverMessage(httpError(401))).not.toContain('status code')
  })
})

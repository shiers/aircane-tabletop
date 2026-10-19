import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import * as api from '../api'
import { useSessionStore } from '../store'
import { loadParticipantSession, saveParticipantSession } from '../participantSession'

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof api>()
  return { ...actual, joinSession: vi.fn() }
})

const joinSession = vi.mocked(api.joinSession)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

describe('session store: joining', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('remembers the participant (token, id, name, session) so a reload resumes', async () => {
    joinSession.mockResolvedValue({
      participantId: 'p-1',
      displayName: 'Thorin',
      role: api.ParticipantRole.Player,
      isApproved: false,
      participantToken: 'token-1',
    })

    await useSessionStore().joinSession('session-1', { displayName: 'Thorin', inviteCode: 'ABCD1234' })

    expect(loadParticipantSession()).toEqual({
      sessionId: 'session-1',
      participantId: 'p-1',
      displayName: 'Thorin',
      token: 'token-1',
    })
  })

  it('forgets an earlier login before asking, so a stale token cannot ride along or block the join', async () => {
    saveParticipantSession({ sessionId: 'old', participantId: 'old-p', displayName: 'Old', token: 'old-token' })
    joinSession.mockImplementation(async () => {
      expect(sessionStorage.getItem('participant_token')).toBeNull()
      throw httpError(400, { detail: 'Invalid invite code.' })
    })

    await expect(useSessionStore().joinSession('session-1', { displayName: 'Thorin', inviteCode: 'WRONG' })).rejects.toBeTruthy()

    expect(loadParticipantSession()).toBeNull()
  })

  it("stores a message a player can act on, not axios's status-code text", async () => {
    joinSession.mockRejectedValue(httpError(401))
    const store = useSessionStore()

    await expect(store.joinSession('session-1', { displayName: 'Thorin', inviteCode: 'X' })).rejects.toBeTruthy()

    expect(store.error).not.toContain('Request failed')
    expect(store.error).toContain('did not accept')
  })
})

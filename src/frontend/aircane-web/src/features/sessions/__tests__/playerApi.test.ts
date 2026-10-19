import { describe, it, expect, vi, beforeEach } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import apiClient from '@/shared/api/client'
import { getAdventureRun, reconnectSession, submitPlayerAction } from '../api'

vi.mock('@/shared/api/client', () => ({
  default: { get: vi.fn(), post: vi.fn() },
}))

const get = vi.mocked(apiClient.get)
const post = vi.mocked(apiClient.post)

describe('player API', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('submitPlayerAction posts { actionText } to the run and returns only the narration', async () => {
    // The real response also carries the full run state and, for a host, the AI's private DM note.
    post.mockResolvedValue({
      data: {
        narration: 'The torch gutters.',
        privateDmNote: 'DM ONLY: the door is trapped.',
        actions: [{ actionType: 'RequestRoll', outcome: 'Applied', errorMessage: null }],
        state: { npcStates: [{ name: 'HIDDEN NPC' }], revealedSecrets: [{ title: 'SECRET' }] },
      },
    })

    const result = await submitPlayerAction('run-1', 'I look around.')

    expect(post).toHaveBeenCalledWith(
      '/api/adventure-sessions/run-1/player-action',
      { actionText: 'I look around.' },
      { timeout: 180_000 },
    )
    expect(result).toEqual({ narration: 'The torch gutters.' })
    expect(JSON.stringify(result)).not.toContain('DM ONLY')
    expect(JSON.stringify(result)).not.toContain('HIDDEN NPC')
  })

  it('getAdventureRun reads the player-safe endpoint of the play session', async () => {
    get.mockResolvedValue({ data: { runId: 'run-1' } })

    const run = await getAdventureRun('session-1')

    expect(get).toHaveBeenCalledWith('/api/sessions/session-1/adventure-run')
    expect(run).toMatchObject({ runId: 'run-1' })
  })

  it('getAdventureRun answers null when no adventure is running (404) and rethrows anything else', async () => {
    const response404 = { status: 404, data: undefined, statusText: '', headers: {}, config: {} } as AxiosResponse
    get.mockRejectedValueOnce(new AxiosError('nope', 'ERR_BAD_REQUEST', undefined, undefined, response404))
    expect(await getAdventureRun('session-1')).toBeNull()

    const response403 = { status: 403, data: undefined, statusText: '', headers: {}, config: {} } as AxiosResponse
    get.mockRejectedValueOnce(new AxiosError('forbidden', 'ERR_BAD_REQUEST', undefined, undefined, response403))
    await expect(getAdventureRun('session-1')).rejects.toBeInstanceOf(AxiosError)
  })

  it('reconnectSession posts to the session\'s reconnect endpoint', async () => {
    post.mockResolvedValue({ data: { isApproved: true } })

    const result = await reconnectSession('session-1')

    expect(post).toHaveBeenCalledWith('/api/sessions/session-1/reconnect')
    expect(result.isApproved).toBe(true)
  })
})

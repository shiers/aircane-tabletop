import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import JoinSessionView from '../JoinSessionView.vue'
import * as sessionsApi from '../api'
import { saveParticipantSession, loadParticipantSession } from '../participantSession'
import vBgAsset from '@/directives/vBgAsset'

/**
 * The /sessions index (the sidebar's "Join Session"): it has no table in its address. A player who is already at
 * one must be taken back to it, not left on a blank join form that looks like being kicked out.
 */

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return { ...actual, getJoinInfo: vi.fn(), joinSession: vi.fn(), reconnectSession: vi.fn() }
})

const replace = vi.hoisted(() => vi.fn())
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRoute: () => ({ params: {} }), useRouter: () => ({ replace }) }
})

const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)
const mockedJoinInfo = vi.mocked(sessionsApi.getJoinInfo)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

function tokenExpiring(expSeconds: number): string {
  return `h.${btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '')}.s`
}

function rememberThorin(token = tokenExpiring(Math.floor(Date.now() / 1000) + 3600)): void {
  saveParticipantSession({ sessionId: 'session-abc', participantId: 'participant-1', displayName: 'Thorin', token })
}

const status = (isApproved: boolean): sessionsApi.ReconnectResult => ({
  sessionId: 'session-abc',
  participantId: 'participant-1',
  displayName: 'Thorin',
  role: sessionsApi.ParticipantRole.Player,
  isApproved,
})

async function mountIndex() {
  const wrapper = mount(JoinSessionView, {
    global: {
      plugins: [createPinia()],
      directives: { 'bg-asset': vBgAsset },
      stubs: { RouterLink: { props: ['to'], template: '<a data-stub="link"><slot /></a>' } },
    },
  })
  await flushPromises()
  return wrapper
}

describe('JoinSessionView at /sessions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('takes an approved player straight back to their session', async () => {
    rememberThorin()
    mockedReconnect.mockResolvedValue(status(true))

    await mountIndex()

    expect(mockedReconnect).toHaveBeenCalledWith('session-abc')
    expect(replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'session-abc' } })
  })

  it('takes a player who is still waiting to the join page of their table, which shows the waiting screen', async () => {
    rememberThorin()
    mockedReconnect.mockResolvedValue(status(false))

    await mountIndex()

    expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-abc' } })
  })

  it('takes a player the host declined to the join page of their table, which shows the denial', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(httpError(403, { code: 'participant-denied' }))

    await mountIndex()

    expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-abc' } })
    expect(loadParticipantSession()).not.toBeNull()   // the join page needs it to show the denial
  })

  it('shows a "Return to your session" card instead of a blank form while it finds out, and if the host cannot be reached', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(new AxiosError('Network Error', 'ERR_NETWORK'))

    const wrapper = await mountIndex()

    const card = wrapper.find('[data-testid="return-to-session"]')
    expect(card.exists()).toBe(true)
    expect(card.text()).toContain('Return to your session')
    expect(card.text()).toContain('Thorin')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(replace).not.toHaveBeenCalled()
    expect(loadParticipantSession()).not.toBeNull()
  })

  it('forgets a login the server no longer accepts, and shows the page as for anyone new (never blocks a fresh join)', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(httpError(401))

    const wrapper = await mountIndex()

    expect(wrapper.find('[data-testid="return-to-session"]').exists()).toBe(false)
    expect(wrapper.find('form').exists()).toBe(true)
    expect(loadParticipantSession()).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
    expect(replace).not.toHaveBeenCalled()
  })

  it('ignores a login whose token has expired, without asking the server', async () => {
    rememberThorin(tokenExpiring(Math.floor(Date.now() / 1000) - 3600))

    const wrapper = await mountIndex()

    expect(mockedReconnect).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="return-to-session"]').exists()).toBe(false)
    expect(wrapper.find('form').exists()).toBe(true)
    expect(loadParticipantSession()).toBeNull()
  })

  it('does nothing for someone with no remembered login', async () => {
    const wrapper = await mountIndex()

    expect(mockedReconnect).not.toHaveBeenCalled()
    expect(mockedJoinInfo).not.toHaveBeenCalled()   // there is no table to look up
    expect(replace).not.toHaveBeenCalled()
    expect(wrapper.find('form').exists()).toBe(true)
  })
})

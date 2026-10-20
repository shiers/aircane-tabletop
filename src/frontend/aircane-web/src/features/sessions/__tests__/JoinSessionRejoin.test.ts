import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import JoinSessionView from '../JoinSessionView.vue'
import * as sessionsApi from '../api'
import { loadParticipantSession } from '../participantSession'
import { getOrCreateRejoinSecret, getRejoinSecret } from '../rejoinSecrets'
import { useSessionStore } from '../store'
import { DENIED_MESSAGE } from '../playerErrors'
import vBgAsset from '@/directives/vBgAsset'

/**
 * /join/{id} opened in a tab with no login (the old tab was closed, or the link opened in a new one): the browser's
 * rejoin secret takes the same seat back before any join form is shown.
 */

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    getJoinInfo: vi.fn(),
    joinSession: vi.fn(),
    reconnectSession: vi.fn(),
    rejoinSession: vi.fn(),
  }
})

vi.mock('../playerHub', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../playerHub')>()
  return {
    ...actual,
    connectPlayerHub: vi.fn(async (options: { onConnectionChange?: (c: boolean) => void }) => {
      options.onConnectionChange?.(true)
      return { connection: {}, sendChat: vi.fn(), stop: vi.fn().mockResolvedValue(undefined) }
    }),
  }
})

const replace = vi.hoisted(() => vi.fn())
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ params: { sessionId: 'session-abc' }, query: {} }),
    useRouter: () => ({ replace, push: vi.fn() }),
  }
})

const mockedRejoin = vi.mocked(sessionsApi.rejoinSession)
const mockedJoin = vi.mocked(sessionsApi.joinSession)
const mockedJoinInfo = vi.mocked(sessionsApi.getJoinInfo)
const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

const seat = (isApproved: boolean): sessionsApi.RejoinSessionResult => ({
  participantId: 'participant-1',
  displayName: 'Bifur',
  role: sessionsApi.ParticipantRole.Player,
  isApproved,
  status: isApproved ? 'approved' : 'pending',
  characterId: null,
  sessionName: 'Friday Table',
  participantToken: 'fresh-token',
})

const mounted: Array<{ unmount: () => void }> = []

async function mountView() {
  const wrapper = mount(JoinSessionView, {
    global: { plugins: [createPinia()], directives: { 'bg-asset': vBgAsset }, stubs: { RouterLink: true } },
  })
  mounted.push(wrapper)
  await flushPromises()
  return wrapper
}

describe('JoinSessionView with a rejoin secret and no login in the tab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    localStorage.clear()
    mockedJoinInfo.mockResolvedValue({ name: 'Friday Table', joinable: true })
    mockedReconnect.mockResolvedValue({
      sessionId: 'session-abc', participantId: 'participant-1', displayName: 'Bifur',
      role: sessionsApi.ParticipantRole.Player, isApproved: false,
    })
  })

  afterEach(() => {
    mounted.splice(0).forEach((w) => w.unmount())
  })

  it('goes straight back into the session when the seat is approved, with no new request', async () => {
    const secret = getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockResolvedValue(seat(true))

    const wrapper = await mountView()

    expect(mockedRejoin).toHaveBeenCalledWith('session-abc', secret)
    expect(mockedJoin).not.toHaveBeenCalled()
    expect(replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'session-abc' } })
    expect(loadParticipantSession()?.token).toBe('fresh-token')
    expect(wrapper.find('#join-display-name').exists()).toBe(false)
  })

  it('shows the waiting screen, with the name and the status, when the request is still pending', async () => {
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockResolvedValue(seat(false))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="waiting-for-approval"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="waiting-name"]').text()).toBe('Welcome back, Bifur!')
    expect(wrapper.find('[data-testid="pending-message"]').text()).toContain('still waiting for the host')
    expect(mockedJoin).not.toHaveBeenCalled()
    expect(replace).not.toHaveBeenCalled()
  })

  it('shows the join form, and forgets the secret, when the server has no seat for it', async () => {
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockRejectedValue(httpError(404, { title: 'No place to return to' }))

    const wrapper = await mountView()

    expect(wrapper.find('#join-display-name').exists()).toBe(true)
    expect(wrapper.find('[data-testid="rejoin-notice"]').exists()).toBe(false)
    expect(getRejoinSecret('session-abc')).toBeNull()
  })

  it('shows the join form with the reason, and forgets the secret, when the host declined it', async () => {
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockRejectedValue(httpError(403, { title: 'Join request declined', code: 'participant-denied' }))

    const wrapper = await mountView()

    expect(wrapper.find('#join-display-name').exists()).toBe(true)
    expect(wrapper.find('[data-testid="rejoin-notice"]').text()).toBe(DENIED_MESSAGE)
    expect(getRejoinSecret('session-abc')).toBeNull()
  })

  it('a fresh join after a stale secret sends a new secret, not the dead one', async () => {
    const stale = getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockRejectedValue(httpError(404))
    mockedJoin.mockResolvedValue({ participantId: 'p-2', displayName: 'Bifur', role: sessionsApi.ParticipantRole.Player, isApproved: false, participantToken: 't' })
    const wrapper = await mountView()

    await wrapper.find('#join-display-name').setValue('Bifur')
    await wrapper.find('#join-invite-code').setValue('ABC123')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const sent = mockedJoin.mock.calls[0][1].rejoinSecret
    expect(sent).toMatch(/^[A-Za-z0-9_-]{22}$/)
    expect(sent).not.toBe(stale)
  })

  it('keeps the secret, and still offers the form, when the host cannot be reached', async () => {
    const secret = getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockRejectedValue(new AxiosError('Network Error', 'ERR_NETWORK'))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="rejoin-notice"]').text()).toContain("Can't reach the host")
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
    expect(getRejoinSecret('session-abc')).toBe(secret)
  })

  it('does not try to rejoin without a secret', async () => {
    const wrapper = await mountView()

    expect(mockedRejoin).not.toHaveBeenCalled()
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
  })

  it('never shows an error another screen left behind, such as "Request failed with status code 401"', async () => {
    useSessionStore().error = 'Request failed with status code 401'

    const wrapper = await mountView()

    expect(wrapper.text()).not.toContain('Request failed with status code')
  })
})

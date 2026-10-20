import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import JoinSessionView from '../JoinSessionView.vue'
import * as sessionsApi from '../api'
import { saveParticipantSession, loadParticipantSession } from '../participantSession'
import vBgAsset from '@/directives/vBgAsset'

// The player has no token yet: the page must use only the anonymous join read.
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    getJoinInfo: vi.fn(),
    getSession: vi.fn(),
    joinSession: vi.fn(),
    reconnectSession: vi.fn(),
    rejoinSession: vi.fn(),
  }
})

// The live connection is the hub's business (see approvalWatch.test.ts); here it just comes up.
const hubEmit = vi.hoisted(() => ({ handlers: {} as Record<string, (payload: unknown) => void> }))
vi.mock('../playerHub', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../playerHub')>()
  return {
    ...actual,
    connectPlayerHub: vi.fn(async (options: { handlers: Record<string, (payload: unknown) => void>; onConnectionChange?: (c: boolean) => void }) => {
      hubEmit.handlers = options.handlers
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
    useRoute: () => ({ params: { sessionId: 'session-abc' } }),
    useRouter: () => ({ replace }),
  }
})

const mockedJoinInfo = vi.mocked(sessionsApi.getJoinInfo)
const mockedGetSession = vi.mocked(sessionsApi.getSession)
const mockedJoin = vi.mocked(sessionsApi.joinSession)
const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

/** A JWT-shaped token whose payload carries `exp` (seconds since the epoch). */
function tokenExpiring(expSeconds: number): string {
  return `h.${btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '')}.s`
}
const LIVE_TOKEN = tokenExpiring(Math.floor(Date.now() / 1000) + 3600)
const EXPIRED_TOKEN = tokenExpiring(Math.floor(Date.now() / 1000) - 3600)

const reconnectResult = (isApproved: boolean): sessionsApi.ReconnectResult => ({
  sessionId: 'session-abc',
  participantId: 'participant-1',
  displayName: 'Thorin',
  role: sessionsApi.ParticipantRole.Player,
  isApproved,
})

function rememberThorin(overrides: { sessionId?: string; token?: string } = {}): void {
  saveParticipantSession({
    sessionId: overrides.sessionId ?? 'session-abc',
    participantId: 'participant-1',
    displayName: 'Thorin',
    token: overrides.token ?? LIVE_TOKEN,
  })
}

const mounted: Array<{ unmount: () => void }> = []

async function mountView() {
  const wrapper = mount(JoinSessionView, {
    global: {
      plugins: [createPinia()],
      directives: { 'bg-asset': vBgAsset },
      stubs: { RouterLink: true },
    },
  })
  mounted.push(wrapper)
  await flushPromises()
  return wrapper
}

describe('JoinSessionView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
    localStorage.clear()
    hubEmit.handlers = {}
    mockedJoinInfo.mockResolvedValue({ name: 'Friday Table', joinable: true })
  })

  afterEach(() => {
    mounted.splice(0).forEach((w) => w.unmount())
    vi.useRealTimers()
  })

  // ── The form ──────────────────────────────────────────────────────────────

  it('reads the anonymous join info, never the authenticated session', async () => {
    await mountView()

    expect(mockedJoinInfo).toHaveBeenCalledWith('session-abc')
    expect(mockedGetSession).not.toHaveBeenCalled()
  })

  it('shows the session name and no error for a joinable session', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Friday Table')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('shows the not-found panel, beside a working form, when the session is not joinable', async () => {
    mockedJoinInfo.mockResolvedValue({ name: null, joinable: false })

    const wrapper = await mountView()

    expect(wrapper.find('[role="alert"]').text()).toContain('no longer open')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('shows a lookup error when the request itself fails', async () => {
    mockedJoinInfo.mockRejectedValue(new Error('Network Error'))

    const wrapper = await mountView()

    expect(wrapper.find('[role="alert"]').text()).toContain('Could not look up this session')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('has no QR code or "Join by QR Code" section: the player is already on the join page', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).not.toContain('Join by QR Code')
    expect(wrapper.findComponent({ name: 'QRCode' }).exists()).toBe(false)
    expect(wrapper.find('canvas').exists()).toBe(false)
  })

  // ── A fresh join ──────────────────────────────────────────────────────────

  async function fillAndSubmit(wrapper: Awaited<ReturnType<typeof mountView>>) {
    await wrapper.find('#join-display-name').setValue('Thorin')
    await wrapper.find('#join-invite-code').setValue('ABCD1234')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
  }

  it('goes straight to the session when the join needs no approval', async () => {
    mockedJoin.mockResolvedValue({
      participantId: 'participant-1',
      displayName: 'Thorin',
      role: sessionsApi.ParticipantRole.Player,
      isApproved: true,
      participantToken: LIVE_TOKEN,
    })
    const wrapper = await mountView()

    await fillAndSubmit(wrapper)

    expect(replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'session-abc' } })
  })

  it('waits for the host when the join needs approval, and remembers who joined', async () => {
    mockedJoin.mockResolvedValue({
      participantId: 'participant-1',
      displayName: 'Thorin',
      role: sessionsApi.ParticipantRole.Player,
      isApproved: false,
      participantToken: LIVE_TOKEN,
    })
    mockedReconnect.mockResolvedValue(reconnectResult(false))
    const wrapper = await mountView()

    await fillAndSubmit(wrapper)

    expect(wrapper.find('[data-testid="waiting-for-approval"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('pending host approval')
    expect(replace).not.toHaveBeenCalled()
    expect(loadParticipantSession()).toMatchObject({ sessionId: 'session-abc', participantId: 'participant-1', token: LIVE_TOKEN })
  })

  it('moves from waiting to the session when the hub reports approval, without a reload', async () => {
    mockedJoin.mockResolvedValue({
      participantId: 'participant-1',
      displayName: 'Thorin',
      role: sessionsApi.ParticipantRole.Player,
      isApproved: false,
      participantToken: LIVE_TOKEN,
    })
    mockedReconnect.mockResolvedValue(reconnectResult(false))
    const wrapper = await mountView()
    await fillAndSubmit(wrapper)

    hubEmit.handlers.ParticipantApproved({ sessionId: 'session-abc', participantId: 'participant-1', displayName: 'Thorin', autoApproved: false })
    await flushPromises()

    expect(replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'session-abc' } })
  })

  it('shows the server\'s reason, not "Request failed with status code 401", when the join fails', async () => {
    mockedJoin.mockRejectedValue(httpError(400, { title: 'Invalid invite code', detail: 'Invalid invite code.' }))
    const wrapper = await mountView()

    await fillAndSubmit(wrapper)

    const alert = wrapper.find('[role="alert"]')
    expect(alert.text()).toContain("invite code isn't right")
    expect(wrapper.text()).not.toContain('status code')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('shows a readable message when the session has ended or is gone', async () => {
    mockedJoin.mockRejectedValueOnce(httpError(400, { detail: "Session 'abc' is not active." }))
    const wrapper = await mountView()
    await fillAndSubmit(wrapper)
    expect(wrapper.find('[role="alert"]').text()).toContain('not open for joining')

    mockedJoin.mockRejectedValueOnce(httpError(404))
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.find('[role="alert"]').text()).toContain('session was not found')
  })

  // ── A reload: the player's place is never lost ───────────────────────────

  it('reload while waiting: shows the waiting screen, not the form, and does not look the session up again', async () => {
    rememberThorin()
    mockedReconnect.mockResolvedValue(reconnectResult(false))

    const wrapper = await mountView()

    expect(mockedReconnect).toHaveBeenCalledWith('session-abc')
    expect(wrapper.find('[data-testid="waiting-for-approval"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Thorin')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(mockedJoinInfo).not.toHaveBeenCalled()
    expect(replace).not.toHaveBeenCalled()
  })

  it('reload while approved: goes straight to the session view without re-approval', async () => {
    rememberThorin()
    mockedReconnect.mockResolvedValue(reconnectResult(true))

    const wrapper = await mountView()

    expect(replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'session-abc' } })
    expect(wrapper.find('form').exists()).toBe(false)
    expect(mockedJoin).not.toHaveBeenCalled()
  })

  it('reload after denial: shows the denied message and stops, with no form until the player chooses', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(httpError(403, { title: 'Join request declined', code: 'participant-denied' }))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="denied-message"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('declined')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(replace).not.toHaveBeenCalled()
  })

  it('"Join again" after a denial forgets the old login and shows a fresh form', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(httpError(403, { code: 'participant-denied' }))
    const wrapper = await mountView()

    await wrapper.find('[data-testid="waiting-for-approval"] button').trigger('click')
    await flushPromises()

    expect(loadParticipantSession()).toBeNull()
    expect(wrapper.find('form').exists()).toBe(true)
    expect(wrapper.find('[data-testid="denied-message"]').exists()).toBe(false)
  })

  it('reload with an expired token: clears it and shows the form, without asking the server', async () => {
    rememberThorin({ token: EXPIRED_TOKEN })

    const wrapper = await mountView()

    expect(mockedReconnect).not.toHaveBeenCalled()
    expect(loadParticipantSession()).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('reload with a token the server rejects (revoked, restarted): clears it and shows the form', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(httpError(401))

    const wrapper = await mountView()

    expect(loadParticipantSession()).toBeNull()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
    expect(wrapper.find('form').exists()).toBe(true)
    expect(mockedJoinInfo).toHaveBeenCalledWith('session-abc')
  })

  it('a login left over from another session never blocks a fresh join here', async () => {
    rememberThorin({ sessionId: 'some-other-session' })

    const wrapper = await mountView()

    expect(mockedReconnect).not.toHaveBeenCalled()
    expect(sessionStorage.getItem('participant_token')).toBeNull()
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('a stale token is not sent with a fresh join: it is cleared first', async () => {
    mockedJoin.mockImplementation(async () => {
      // At the moment of the request the old token must be gone, or the API client would attach it.
      expect(sessionStorage.getItem('participant_token')).toBeNull()
      return {
        participantId: 'participant-2',
        displayName: 'Thorin',
        role: sessionsApi.ParticipantRole.Player,
        isApproved: true,
        participantToken: LIVE_TOKEN,
      }
    })
    sessionStorage.setItem('participant_token', 'stale-token-without-an-identity')
    const wrapper = await mountView()

    await fillAndSubmit(wrapper)

    expect(mockedJoin).toHaveBeenCalled()
  })

  it('reload while the host cannot be reached: keeps the waiting screen and says it is still trying', async () => {
    rememberThorin()
    mockedReconnect.mockRejectedValue(new AxiosError('Network Error', 'ERR_NETWORK'))

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="waiting-for-approval"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="connection-problem"]').exists()).toBe(true)
    expect(wrapper.find('form').exists()).toBe(false)
    expect(loadParticipantSession()).not.toBeNull()
  })
})

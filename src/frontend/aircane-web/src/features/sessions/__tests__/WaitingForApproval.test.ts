import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { AxiosError, type AxiosResponse } from 'axios'
import WaitingForApproval from '../components/WaitingForApproval.vue'
import type { ReconnectResult } from '../api'
import type { PlayerHub, PlayerHubOptions } from '../playerHub'

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

const status = (isApproved: boolean): ReconnectResult => ({
  sessionId: 'session-1',
  participantId: 'me',
  displayName: 'Thorin',
  role: 2,
  isApproved,
})

const mounted: Array<{ unmount: () => void }> = []

function setup(check: () => Promise<ReconnectResult>) {
  let options: PlayerHubOptions | null = null
  const stop = vi.fn().mockResolvedValue(undefined)
  const connect = vi.fn(async (opts: PlayerHubOptions) => {
    options = opts
    opts.onConnectionChange?.(true)
    return { connection: {}, sendChat: vi.fn(), stop } as unknown as PlayerHub
  })
  const wrapper = mount(WaitingForApproval, {
    props: {
      sessionId: 'session-1',
      participantId: 'me',
      displayName: 'Thorin',
      watchOptions: { check, connect },
    },
  })
  mounted.push(wrapper)
  return {
    wrapper,
    connect,
    stop,
    emit: (name: string, payload: unknown) => (options!.handlers[name] as (p: unknown) => void)(payload),
  }
}

describe('WaitingForApproval', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })
  afterEach(() => {
    mounted.splice(0).forEach((w) => w.unmount())
    vi.useRealTimers()
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' })
  })

  it("shows the pending message with the player's name", async () => {
    const { wrapper } = setup(vi.fn().mockResolvedValue(status(false)))
    await flushPromises()

    expect(wrapper.text()).toContain('Welcome, Thorin!')
    expect(wrapper.find('[data-testid="pending-message"]').text()).toContain('pending host approval')
  })

  it('emits approved when the hub says this participant was approved', async () => {
    const { wrapper, emit } = setup(vi.fn().mockResolvedValue(status(false)))
    await flushPromises()

    emit('ParticipantApproved', { sessionId: 'session-1', participantId: 'me', displayName: 'Thorin', autoApproved: false })
    await flushPromises()

    expect(wrapper.emitted('approved')).toHaveLength(1)
  })

  it('emits approved when a reload finds the participant already approved', async () => {
    const { wrapper } = setup(vi.fn().mockResolvedValue(status(true)))
    await flushPromises()

    expect(wrapper.emitted('approved')).toHaveLength(1)
  })

  it('shows a clear message and stops when denied; "Join again" asks the parent to start over', async () => {
    const { wrapper, emit, stop } = setup(vi.fn().mockResolvedValue(status(false)))
    await flushPromises()

    emit('ParticipantDenied', { sessionId: 'session-1', participantId: 'me', displayName: 'Thorin' })
    await flushPromises()

    expect(wrapper.find('[data-testid="denied-message"]').text()).toContain('declined')
    expect(wrapper.find('[data-testid="pending-message"]').exists()).toBe(false)
    expect(stop).toHaveBeenCalled()
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('rejoin')).toHaveLength(1)
    expect(wrapper.emitted('approved')).toBeUndefined()
  })

  it('emits rejected when the server no longer accepts the login', async () => {
    const { wrapper } = setup(vi.fn().mockRejectedValue(httpError(401)))
    await flushPromises()

    expect(wrapper.emitted('rejected')).toHaveLength(1)
  })

  it('looks again, now, when the page becomes visible again (events sent while it was in the background are lost)', async () => {
    const check = vi.fn().mockResolvedValue(status(false))
    const { wrapper } = setup(check)
    await flushPromises()
    check.mockClear()

    // Approved while the phone was in another app.
    check.mockResolvedValue(status(true))
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(check).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('approved')).toHaveLength(1)
  })

  it('opens the live connection again on return if it had dropped', async () => {
    const check = vi.fn().mockResolvedValue(status(false))
    const { connect } = setup(check)
    await flushPromises()
    expect(connect).toHaveBeenCalledTimes(1)

    // The connection dropped while backgrounded.
    const firstOptions = connect.mock.calls[0][0] as PlayerHubOptions
    firstOptions.onConnectionChange?.(false)
    await flushPromises()
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(connect).toHaveBeenCalledTimes(2)
  })

  it('stops listening for visibility when it goes away', async () => {
    const check = vi.fn().mockResolvedValue(status(false))
    const { wrapper } = setup(check)
    await flushPromises()
    check.mockClear()

    wrapper.unmount()
    document.dispatchEvent(new Event('visibilitychange'))
    await flushPromises()

    expect(check).not.toHaveBeenCalled()
  })
})

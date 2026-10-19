import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import { useApprovalWatch } from '../approvalWatch'
import type { ReconnectResult } from '../api'
import type { PlayerHub, PlayerHubOptions } from '../playerHub'

const ME = { participantId: 'me', displayName: 'Thorin' }

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

/** A hub that records what the watch asked of it and lets the test play the server. */
function fakeHub() {
  const stop = vi.fn().mockResolvedValue(undefined)
  let options: PlayerHubOptions | null = null
  const connect = vi.fn(async (opts: PlayerHubOptions) => {
    options = opts
    opts.onConnectionChange?.(true)
    return { connection: {}, sendChat: vi.fn(), stop } as unknown as PlayerHub
  })
  return {
    connect,
    stop,
    /** The server sends an event to the group. */
    emit(name: string, payload: unknown) {
      ;(options!.handlers[name] as (p: unknown) => void)(payload)
    },
    /** The connection drops. */
    drop() {
      options!.onConnectionChange?.(false)
    },
    reconnected() {
      options!.onConnectionChange?.(true)
      options!.onReconnected?.()
    },
  }
}

const flush = async () => {
  await vi.advanceTimersByTimeAsync(0)
}

describe('useApprovalWatch', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts by checking, then waits when the host has not decided', async () => {
    const check = vi.fn().mockResolvedValue(status(false))
    const hub = fakeHub()
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })

    expect(watch.state.value).toBe('checking')
    watch.start()
    await flush()

    expect(watch.state.value).toBe('waiting')
    watch.stop()
  })

  it('moves to approved when the hub reports this participant approved, with no reload', async () => {
    const hub = fakeHub()
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(false)),
      connect: hub.connect,
    })
    watch.start()
    await flush()

    hub.emit('ParticipantApproved', { sessionId: 'session-1', participantId: 'me', displayName: 'Thorin', autoApproved: false })

    expect(watch.state.value).toBe('approved')
  })

  it('joins the hub group as this participant (the connect options carry the identity)', async () => {
    const hub = fakeHub()
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(false)),
      connect: hub.connect,
    })
    watch.start()
    await flush()

    expect(hub.connect).toHaveBeenCalledWith(expect.objectContaining({ sessionId: 'session-1', me: ME }))
    watch.stop()
  })

  it('ignores approval and denial of other participants', async () => {
    const hub = fakeHub()
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(false)),
      connect: hub.connect,
    })
    watch.start()
    await flush()

    hub.emit('ParticipantApproved', { sessionId: 'session-1', participantId: 'someone-else', displayName: 'Bob', autoApproved: false })
    hub.emit('ParticipantDenied', { sessionId: 'session-1', participantId: 'someone-else', displayName: 'Mallory' })

    expect(watch.state.value).toBe('waiting')
    watch.stop()
  })

  it('moves to denied when the hub reports this participant denied, and stops everything', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockResolvedValue(status(false))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })
    watch.start()
    await flush()

    hub.emit('ParticipantDenied', { sessionId: 'session-1', participantId: 'me', displayName: 'Thorin' })

    expect(watch.state.value).toBe('denied')
    expect(hub.stop).toHaveBeenCalled()
    const callsAtDenial = check.mock.calls.length
    await vi.advanceTimersByTimeAsync(120_000)
    expect(check.mock.calls.length).toBe(callsAtDenial)
  })

  it('falls back to polling when the hub cannot connect, and still gets to approved', async () => {
    const connect = vi.fn().mockRejectedValue(new Error('hub down'))
    const check = vi.fn().mockResolvedValueOnce(status(false)).mockResolvedValue(status(true))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect, pollWhenHubDownMs: 4_000 })
    watch.start()
    await flush()
    expect(watch.state.value).toBe('waiting')
    expect(watch.hubConnected.value).toBe(false)

    await vi.advanceTimersByTimeAsync(4_000)

    expect(watch.state.value).toBe('approved')
  })

  it('polls quickly while the hub is down and slowly while it is up', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockResolvedValue(status(false))
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check,
      connect: hub.connect,
      pollWhenHubDownMs: 4_000,
      pollWhenHubUpMs: 20_000,
    })
    watch.start()
    await flush()
    check.mockClear()

    // Hub up: nothing for the first 19 seconds, then one poll.
    await vi.advanceTimersByTimeAsync(19_000)
    expect(check).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1_000)
    expect(check).toHaveBeenCalledTimes(1)

    // The hub drops: an immediate poll, and then every 4 seconds.
    check.mockClear()
    hub.drop()
    await flush()
    expect(check).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(8_000)
    expect(check.mock.calls.length).toBeGreaterThanOrEqual(3)
    watch.stop()
  })

  it('catches up with a poll when the hub reconnects', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockResolvedValue(status(false))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })
    watch.start()
    await flush()
    check.mockClear()

    // Approved while the connection was down: the event was missed, the poll finds it.
    check.mockResolvedValue(status(true))
    hub.reconnected()
    await flush()

    expect(watch.state.value).toBe('approved')
  })

  it('moves to denied when the poll gets the participant-denied refusal (a reload after a denial)', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockRejectedValue(httpError(403, { title: 'Join request declined', code: 'participant-denied' }))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })

    watch.start()
    await flush()

    expect(watch.state.value).toBe('denied')
  })

  it('moves to rejected when the server no longer accepts the token (401)', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockRejectedValue(httpError(401))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })

    watch.start()
    await flush()

    expect(watch.state.value).toBe('rejected')
  })

  it('keeps waiting, and says the host cannot be reached, when nothing answers', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockRejectedValueOnce(new AxiosError('Network Error', 'ERR_NETWORK')).mockResolvedValue(status(false))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect, pollWhenHubUpMs: 5_000 })

    watch.start()
    await flush()
    expect(watch.connectionProblem.value).toBe(true)
    expect(watch.state.value).toBe('checking')

    await vi.advanceTimersByTimeAsync(5_000)

    expect(watch.connectionProblem.value).toBe(false)
    expect(watch.state.value).toBe('waiting')
    watch.stop()
  })

  it('approves straight away when the first check says the participant is already approved (a reload)', async () => {
    const hub = fakeHub()
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(true)),
      connect: hub.connect,
    })

    watch.start()
    await flush()

    expect(watch.state.value).toBe('approved')
    expect(hub.stop).toHaveBeenCalled()
  })

  it('closes a connection that finished opening after the watch had already stopped', async () => {
    const stop = vi.fn().mockResolvedValue(undefined)
    let release: (hub: PlayerHub) => void = () => undefined
    const connect = vi.fn(() => new Promise<PlayerHub>((resolve) => (release = resolve)))
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(true)),
      connect,
    })

    watch.start()
    await flush() // approved by the poll while the connection is still opening
    release({ connection: {}, sendChat: vi.fn(), stop } as unknown as PlayerHub)
    await flush()

    expect(stop).toHaveBeenCalled()
  })
})

describe('useApprovalWatch.refresh (the page came back to the foreground)', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('checks right away, whatever the poll schedule says', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockResolvedValue(status(false))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect, pollWhenHubUpMs: 60_000 })
    watch.start()
    await flush()
    check.mockClear()

    check.mockResolvedValue(status(true))
    watch.refresh()
    await flush()

    expect(check).toHaveBeenCalledTimes(1)
    expect(watch.state.value).toBe('approved')
  })

  it('opens a new live connection when the old one is down, and leaves a healthy one alone', async () => {
    const hub = fakeHub()
    const watch = useApprovalWatch({
      sessionId: 'session-1',
      me: ME,
      check: vi.fn().mockResolvedValue(status(false)),
      connect: hub.connect,
    })
    watch.start()
    await flush()
    expect(hub.connect).toHaveBeenCalledTimes(1)

    watch.refresh()
    await flush()
    expect(hub.connect).toHaveBeenCalledTimes(1) // up: nothing to redo

    hub.drop()
    await flush()
    watch.refresh()
    await flush()

    expect(hub.connect).toHaveBeenCalledTimes(2)
    expect(hub.stop).toHaveBeenCalled() // the dead one was closed
    watch.stop()
  })

  it('does nothing once the decision is made', async () => {
    const hub = fakeHub()
    const check = vi.fn().mockResolvedValue(status(true))
    const watch = useApprovalWatch({ sessionId: 'session-1', me: ME, check, connect: hub.connect })
    watch.start()
    await flush()
    check.mockClear()

    watch.refresh()
    await flush()

    expect(check).not.toHaveBeenCalled()
  })
})

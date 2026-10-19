import { ref, type Ref } from 'vue'
import { reconnectSession, type ReconnectResult } from './api'
import {
  connectPlayerHub,
  type ParticipantApprovedEvent,
  type ParticipantDeniedEvent,
  type PlayerHub,
  type PlayerIdentity,
} from './playerHub'
import { isParticipantDenied, isTokenRejected, isUnreachable } from './playerErrors'

/**
 * Where a participant stands with the host.
 *   - `checking`: asking the server for the first time (a reload, or just after joining)
 *   - `waiting`:  joined, the host has not decided yet
 *   - `approved`: let in; the caller moves on to the session view
 *   - `denied`:   the host declined; nothing more will happen
 *   - `rejected`: the server no longer accepts the stored token (expired, revoked, other session);
 *                 the caller forgets it and offers a fresh join
 */
export type ApprovalState = 'checking' | 'waiting' | 'approved' | 'denied' | 'rejected'

export interface ApprovalWatchOptions {
  sessionId: string
  me: PlayerIdentity
  /** The cheap status read; defaults to POST /reconnect, which reports `isApproved`. */
  check?: () => Promise<ReconnectResult>
  /** Opens the live connection; defaults to the real hub. */
  connect?: typeof connectPlayerHub
  /** How often to ask the server while the live connection is down. */
  pollWhenHubDownMs?: number
  /** How often to ask anyway while it is up (a safety net for a missed event). */
  pollWhenHubUpMs?: number
  /** How long to wait before trying to open the live connection again. */
  hubRetryMs?: number
}

export interface ApprovalWatch {
  state: Ref<ApprovalState>
  hubConnected: Ref<boolean>
  /** Set while the host cannot be reached; cleared by the next answer. */
  connectionProblem: Ref<boolean>
  start: () => void
  stop: () => void
  /**
   * Look now, and bring the live connection back if it is down. For when the page returns to the
   * foreground: whatever the host did while it was away arrived on a connection that was not there.
   */
  refresh: () => void
}

/**
 * Waits for the host's decision on a join request.
 *
 * The decision arrives two ways: the hub pushes `ParticipantApproved` / `ParticipantDenied` to the
 * session's group the moment the host acts, and a poll of the cheap reconnect endpoint covers the
 * cases where the hub is down or an event was missed. The poll is frequent while the hub is down
 * and rare while it is up. Events about other participants are ignored. Either way, the first
 * decision wins and everything (timers, connection) stops.
 */
export function useApprovalWatch(options: ApprovalWatchOptions): ApprovalWatch {
  const {
    sessionId,
    me,
    check = () => reconnectSession(sessionId),
    connect = connectPlayerHub,
    pollWhenHubDownMs = 4_000,
    pollWhenHubUpMs = 20_000,
    hubRetryMs = 10_000,
  } = options

  const state = ref<ApprovalState>('checking')
  const hubConnected = ref(false)
  const connectionProblem = ref(false)

  let hub: PlayerHub | null = null
  let pollTimer: ReturnType<typeof setTimeout> | null = null
  let hubTimer: ReturnType<typeof setTimeout> | null = null
  let stopped = true

  const isTerminal = () => state.value === 'approved' || state.value === 'denied' || state.value === 'rejected'

  function finish(next: ApprovalState): void {
    if (isTerminal() || stopped) return
    state.value = next
    stop()
  }

  async function poll(): Promise<void> {
    if (stopped) return
    try {
      const result = await check()
      if (stopped) return
      connectionProblem.value = false
      if (result.isApproved) finish('approved')
      else if (state.value === 'checking') state.value = 'waiting'
    } catch (err) {
      if (stopped) return
      if (isParticipantDenied(err)) finish('denied')
      else if (isTokenRejected(err)) finish('rejected')
      else if (isUnreachable(err)) connectionProblem.value = true
      // Anything else (a 5xx, a 429) is treated as a missed poll: try again next time.
      if (state.value === 'checking' && !isTerminal() && !isUnreachable(err)) state.value = 'waiting'
    }
    scheduleNextPoll()
  }

  function scheduleNextPoll(): void {
    if (stopped || isTerminal()) return
    if (pollTimer) clearTimeout(pollTimer)
    pollTimer = setTimeout(() => void poll(), hubConnected.value ? pollWhenHubUpMs : pollWhenHubDownMs)
  }

  async function openHub(): Promise<void> {
    if (stopped || isTerminal()) return
    try {
      const opened = await connect({
        sessionId,
        me,
        handlers: {
          ParticipantApproved: ((event: ParticipantApprovedEvent) => {
            if (event?.participantId === me.participantId) finish('approved')
          }),
          ParticipantDenied: ((event: ParticipantDeniedEvent) => {
            if (event?.participantId === me.participantId) finish('denied')
          }),
        },
        onConnectionChange: (connected) => {
          hubConnected.value = connected
          // A dropped connection means events may be missed: ask now rather than at the slow pace.
          if (!connected && !stopped && !isTerminal()) void poll()
        },
        // The new connection is in no group until it has rejoined; anything said meanwhile was missed.
        onReconnected: () => void poll(),
      })
      if (stopped || isTerminal()) {
        await opened.stop()
        return
      }
      hub = opened
    } catch {
      hubConnected.value = false
      if (!stopped && !isTerminal()) {
        hubTimer = setTimeout(() => void openHub(), hubRetryMs)
      }
    }
  }

  function start(): void {
    if (!stopped) return
    stopped = false
    void openHub()
    void poll()
  }

  function stop(): void {
    stopped = true
    if (pollTimer) clearTimeout(pollTimer)
    if (hubTimer) clearTimeout(hubTimer)
    pollTimer = null
    hubTimer = null
    const open = hub
    hub = null
    hubConnected.value = false
    if (open) void open.stop()
  }

  function refresh(): void {
    if (stopped || isTerminal()) return
    void poll()
    if (!hubConnected.value) {
      if (hubTimer) clearTimeout(hubTimer)
      hubTimer = null
      const stale = hub
      hub = null
      if (stale) void stale.stop()
      void openHub()
    }
  }

  return { state, hubConnected, connectionProblem, start, stop, refresh }
}

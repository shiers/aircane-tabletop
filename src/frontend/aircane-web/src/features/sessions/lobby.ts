import { ParticipantRole, type NetworkInfo, type ParticipantDto } from './api'

/**
 * Pure rules for the group-play host lobby, kept out of the components so they can be tested.
 */

/**
 * The players who can take a turn: approved, with the Player role. The backend's start-group only
 * accepts approved Player or Host participants, and the host never has a participant record, so
 * in practice this is every approved joiner. Spectators and a human DM do not take turns.
 */
export function eligiblePlayers(participants: readonly ParticipantDto[]): ParticipantDto[] {
  return participants.filter((p) => p.isApproved && p.role === ParticipantRole.Player)
}

/** People waiting to be approved, oldest first. */
export function pendingParticipants(participants: readonly ParticipantDto[]): ParticipantDto[] {
  return participants
    .filter((p) => !p.isApproved)
    .sort((a, b) => a.joinedAt.localeCompare(b.joinedAt))
}

/**
 * Brings the host's turn order in line with who is currently eligible.
 *
 * The order defaults to approval order and the host can then reorder it. Because the server does
 * not record when someone was approved, the order is built as the host works: a player who
 * appears among the eligible is appended after those already listed (players who were already
 * approved when the lobby opened, e.g. after a reload, are appended by join time), and anyone no
 * longer eligible is dropped. Everyone already placed keeps their position, so a manual
 * reordering survives the periodic refresh.
 */
export function reconcileTurnOrder(current: readonly string[], eligible: readonly ParticipantDto[]): string[] {
  const eligibleIds = new Set(eligible.map((p) => p.id))
  const kept = current.filter((id) => eligibleIds.has(id))
  const placed = new Set(kept)
  const added = eligible
    .filter((p) => !placed.has(p.id))
    .sort((a, b) => a.joinedAt.localeCompare(b.joinedAt))
    .map((p) => p.id)
  return [...kept, ...added]
}

/** Moves one player up (-1) or down (+1) the turn order. Out-of-range moves leave it unchanged. */
export function moveInTurnOrder(order: readonly string[], id: string, delta: -1 | 1): string[] {
  const from = order.indexOf(id)
  const to = from + delta
  if (from === -1 || to < 0 || to >= order.length) return [...order]
  const next = [...order]
  next.splice(from, 1)
  next.splice(to, 0, id)
  return next
}

export interface StartState {
  /** How many players are in the turn order. */
  turnOrderCount: number
  /** The start request is in flight. */
  starting: boolean
  /** The play session is still open. */
  sessionOpen: boolean
  /** A group run for this campaign already exists (so starting again would only resume it). */
  runExists: boolean
}

/** Why "Start adventure" is disabled, or null when it is enabled. Shown beside the button. */
export function startDisabledReason(state: StartState): string | null {
  if (!state.sessionOpen) return 'This session has ended.'
  if (state.runExists) return 'This campaign already has a group adventure running.'
  if (state.starting) return 'Starting…'
  if (state.turnOrderCount < 1) return 'Approve at least one player to start.'
  return null
}

export function canStartAdventure(state: StartState): boolean {
  return startDisabledReason(state) === null
}

export interface JoinLink {
  url: string
  /** False when the link still points at this computer (localhost) and no LAN address was found. */
  reachableFromLan: boolean
}

const LOOPBACK_HOSTS = new Set(['localhost', '127.0.0.1', '[::1]', '::1'])

/**
 * The link players on the same network should open: the page the host is looking at, but with the
 * host computer's LAN address in place of `localhost`. Players cannot open `localhost`; it is their
 * own device. The scheme and port come from the host's own page, because that is where the app is
 * served from (the desktop app and the dev server use different ports); the LAN address comes from
 * GET /api/sessions/network-info. If the host already opened the app by a non-loopback address,
 * that address is used as it is.
 */
export function buildJoinLink(origin: string, sessionId: string, networkInfo: NetworkInfo | null): JoinLink {
  let page: URL
  try {
    page = new URL(origin)
  } catch {
    return { url: `/join/${sessionId}`, reachableFromLan: false }
  }

  if (!LOOPBACK_HOSTS.has(page.hostname)) {
    return { url: `${page.origin}/join/${sessionId}`, reachableFromLan: true }
  }

  let lanHost: string | null = null
  try {
    lanHost = networkInfo?.lanUrl ? new URL(networkInfo.lanUrl).hostname : null
  } catch {
    lanHost = null
  }
  if (!lanHost) return { url: `${page.origin}/join/${sessionId}`, reachableFromLan: false }

  const port = page.port ? `:${page.port}` : ''
  return { url: `${page.protocol}//${lanHost}${port}/join/${sessionId}`, reachableFromLan: true }
}

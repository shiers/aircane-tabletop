import axios from 'axios'
import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export enum SessionAccessMode {
  Solo = 0,
  LocalLan = 1,
  InternetTunnel = 2,
  Cloud = 3,
}

export enum SessionStatus {
  Pending = 0,
  Active = 1,
  Paused = 2,
  Ended = 3,
}

/** Mirrors Aircane.Domain.Enums.ParticipantRole, which the API sends as a number. */
export enum ParticipantRole {
  Host = 0,
  HumanDm = 1,
  Player = 2,
  Spectator = 3,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface SessionDto {
  id: string
  campaignId: string
  name: string
  accessMode: SessionAccessMode
  status: SessionStatus
  startedAt: string | null
  endedAt: string | null
  createdAt: string
  participantCount: number
  /** Only present immediately after session creation. */
  inviteCode: string | null
  /** Only present immediately after session creation. */
  joinUrl: string | null
}

export interface StartSessionRequest {
  name: string
  accessMode: SessionAccessMode
  requireHostApproval?: boolean
}

export interface JoinSessionRequest {
  displayName: string
  inviteCode: string
}

export interface JoinSessionResult {
  participantId: string
  displayName: string
  role: ParticipantRole
  isApproved: boolean
  participantToken: string
}

export interface ParticipantDto {
  id: string
  sessionId: string
  displayName: string
  role: ParticipantRole
  characterId: string | null
  isApproved: boolean
  joinedAt: string
  lastSeenAt: string
  /** Only ever true on the response to a denial; denied people are not on the roster. */
  isDenied?: boolean
}

export interface NetworkInfo {
  localUrl: string
  lanUrl: string | null
  inviteCode: string | null
  /** Public Cloudflare Tunnel URL for remote players, when internet play is active. */
  tunnelUrl: string | null
  /** True when an internet tunnel is currently active. */
  tunnelActive: boolean
}

// ---------------------------------------------------------------------------
// Display helpers
// ---------------------------------------------------------------------------

export const sessionAccessModeLabels: Record<SessionAccessMode, string> = {
  [SessionAccessMode.Solo]: 'Solo',
  [SessionAccessMode.LocalLan]: 'Local LAN',
  [SessionAccessMode.InternetTunnel]: 'Internet Tunnel',
  [SessionAccessMode.Cloud]: 'Cloud',
}

export const sessionStatusLabels: Record<SessionStatus, string> = {
  [SessionStatus.Pending]: 'Pending',
  [SessionStatus.Active]: 'Active',
  [SessionStatus.Paused]: 'Paused',
  [SessionStatus.Ended]: 'Ended',
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/** Start a new session under a campaign. Returns the session including the one-time invite code and join URL. */
export async function startSession(
  campaignId: string,
  request: StartSessionRequest,
): Promise<SessionDto> {
  const response = await apiClient.post<SessionDto>(
    `/api/campaigns/${campaignId}/start-session`,
    request,
  )
  return response.data
}

/**
 * What the join page needs before the player has a token. Anonymous: the session name when it can
 * be joined, and nothing else. A session that does not exist, has ended, or is not open to players
 * all come back as `{ name: null, joinable: false }`.
 */
export interface JoinInfo {
  name: string | null
  joinable: boolean
}

/** Read the minimal join information for a session. Needs no token. */
export async function getJoinInfo(sessionId: string): Promise<JoinInfo> {
  const response = await apiClient.get<JoinInfo>(`/api/sessions/${sessionId}/join-info`)
  return response.data
}

/** Get a session by ID. The invite code and join URL are not included in this response. */
export async function getSession(sessionId: string): Promise<SessionDto> {
  const response = await apiClient.get<SessionDto>(`/api/sessions/${sessionId}`)
  return response.data
}

/** Join a session with a display name and invite code. Returns the participant token. */
export async function joinSession(
  sessionId: string,
  request: JoinSessionRequest,
): Promise<JoinSessionResult> {
  const response = await apiClient.post<JoinSessionResult>(
    `/api/sessions/${sessionId}/join`,
    request,
  )
  return response.data
}

/** End a session and optionally save a summary. */
export async function endSession(sessionId: string, summary?: string): Promise<void> {
  await apiClient.post(`/api/sessions/${sessionId}/end`, { summary: summary ?? null })
}

/** Get all participants in a session. */
export async function getParticipants(sessionId: string): Promise<ParticipantDto[]> {
  const response = await apiClient.get<ParticipantDto[]>(
    `/api/sessions/${sessionId}/participants`,
  )
  return response.data
}

/** Approve a pending participant, granting them access to the session. */
export async function approveParticipant(
  sessionId: string,
  participantId: string,
): Promise<ParticipantDto> {
  const response = await apiClient.post<ParticipantDto>(
    `/api/sessions/${sessionId}/approve-participant`,
    { participantId },
  )
  return response.data
}

/** Assign a character to a participant in the session. */
export async function assignCharacter(
  sessionId: string,
  participantId: string,
  characterId: string,
): Promise<ParticipantDto> {
  const response = await apiClient.post<ParticipantDto>(
    `/api/sessions/${sessionId}/assign-character`,
    { participantId, characterId },
  )
  return response.data
}

/** Get the host's reachable URLs (local, LAN, and tunnel when internet play is active). */
export async function getNetworkInfo(): Promise<NetworkInfo> {
  const response = await apiClient.get<NetworkInfo>('/api/sessions/network-info')
  return response.data
}

// ---------------------------------------------------------------------------
// Player side: reconnect, the player-safe adventure run, and acting in it
// ---------------------------------------------------------------------------

/** What POST /api/sessions/{id}/reconnect says about the caller's own token. */
export interface ReconnectResult {
  sessionId: string
  participantId: string
  displayName: string
  role: ParticipantRole
  /** False while the host has not approved this participant yet. */
  isApproved: boolean
}

/**
 * Checks the stored participant token against the server (it is sent as the bearer token by the API
 * client) and reports whether the host has approved this participant. Cheap, so it doubles as the
 * waiting screen's fallback poll. Never needs approval itself.
 */
export async function reconnectSession(sessionId: string): Promise<ReconnectResult> {
  const response = await apiClient.post<ReconnectResult>(`/api/sessions/${sessionId}/reconnect`)
  return response.data
}

/** One player's place in the turn order. The array index is the zero-based turn position. */
export interface PlayerTurn {
  participantId: string
  participantName: string
  isActive: boolean
}

/** A roll the AI DM asked the acting player for and that has not been answered yet. */
export interface PlayerPendingRoll {
  label: string
  /** Null when the DM gave no difficulty. */
  dc: number | null
}

/** One line of the public story so far. */
export interface PlayerNarrationEntry {
  /** "PlayerAction" (what a player typed) or "AiNarration" (what the AI DM said to the table). */
  kind: string
  text: string
  createdAt: string
}

/**
 * GET /api/sessions/{id}/adventure-run: the adventure as a Player may see it (mirrors
 * Aircane.Application.DTOs.Adventures.PlayerAdventureRunDto). The server leaves out DM notes, secrets,
 * NPCs, exits, objectives and the AI's private note, so nothing in this shape is DM-only; every field
 * the view shows comes from here and nowhere else.
 */
export interface PlayerAdventureRun {
  runId: string
  adventureTitle: string
  /** "InProgress" | "Completed" | "Abandoned" | "NotStarted". */
  status: string
  isGroup: boolean
  /** Title and the text meant to be read aloud to the table (null when the scene has none). */
  scene: { title: string; description: string | null }
  currentSceneNumber: number
  totalScenes: number
  activeParticipantId: string | null
  activeParticipantName: string | null
  currentRound: number
  turnOrder: PlayerTurn[]
  /** Only ever set for the participant who owes the roll. */
  pendingRoll: PlayerPendingRoll | null
  recentNarration: PlayerNarrationEntry[]
  /**
   * How the turn moves on: "ManualOnly" (the acting player or the host ends it) or "AfterEachAction" (it
   * moves to the next player by itself once the AI has answered). Missing on an older server: manual.
   */
  turnAdvance?: TurnAdvanceMode
}

export type TurnAdvanceMode = 'ManualOnly' | 'AfterEachAction'

/**
 * Ends the calling player's own turn (POST /api/adventure-sessions/{runId}/advance-turn) and returns the
 * table view of the run. The server refuses (403) anyone who does not hold the turn, which includes a
 * request that arrives after the turn already moved on.
 */
export async function endMyTurn(runId: string): Promise<PlayerAdventureRun | null> {
  const response = await apiClient.post<PlayerAdventureRun>(`/api/adventure-sessions/${runId}/advance-turn`)
  // 204: the turn moved but there was no table view to return; the caller reads the run again.
  return response.status === 204 || !response.data || typeof response.data !== 'object' ? null : response.data
}

/** One stored chat message (GET /api/sessions/{id}/chat). */
export interface ChatHistoryMessage {
  id: string
  sessionId: string
  senderId: string
  senderName: string
  senderRole: ParticipantRole
  text: string
  sentAt: string
}

/** A page of chat history, oldest first. `hasMore`: older messages exist (ask again with `before`). */
export interface ChatHistoryPage {
  messages: ChatHistoryMessage[]
  hasMore: boolean
}

/**
 * The stored chat of a session: the newest `limit` messages (default 100, the server allows 200), or those
 * older than the message `before`. Host and approved participants only.
 */
export async function getChatHistory(
  sessionId: string,
  options: { limit?: number; before?: string } = {},
): Promise<ChatHistoryPage> {
  const response = await apiClient.get<ChatHistoryPage>(`/api/sessions/${sessionId}/chat`, {
    params: { limit: options.limit, before: options.before },
  })
  return response.data
}

/**
 * The adventure running in this play session, as a player may see it, or null when none is running
 * (404). Host and approved participants only.
 */
export async function getAdventureRun(sessionId: string): Promise<PlayerAdventureRun | null> {
  try {
    const response = await apiClient.get<PlayerAdventureRun>(`/api/sessions/${sessionId}/adventure-run`)
    return response.data
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 404) return null
    throw err
  }
}

/** What a player gets back from acting: the narration, and nothing else. */
export interface PlayerActionResult {
  narration: string
}

/**
 * Submits a free-text action to the AI DM for this run (POST /api/adventure-sessions/{runId}/player-action).
 * Only the acting player may; the AI can take a while, hence the long timeout.
 *
 * The response also carries the run's full state and, for the host only, the AI's private DM note.
 * Neither is returned from here: only the public narration is picked out, so nothing DM-only can
 * reach a component. The refreshed run is read through {@link getAdventureRun}.
 */
export async function submitPlayerAction(runId: string, actionText: string): Promise<PlayerActionResult> {
  const response = await apiClient.post<{ narration?: string }>(
    `/api/adventure-sessions/${runId}/player-action`,
    { actionText },
    { timeout: 180_000 },
  )
  return { narration: response.data?.narration ?? '' }
}

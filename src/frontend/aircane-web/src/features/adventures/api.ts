import axios from 'axios'
import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// DTOs (mirror Aircane.Application.DTOs.Adventures.AdventureSessionDtos)
// ---------------------------------------------------------------------------

export type AdventureSessionStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'Abandoned'

export interface StartAdventureSessionRequest {
  generatedAdventureId: string
  campaignId: string
  characterId?: string | null
}

export interface StartGroupAdventureSessionRequest {
  generatedAdventureId: string
  campaignId: string
  /** Approved players of the play session, in turn order. */
  participantIds: string[]
  /**
   * The play session the players joined. The server refuses (409) when the campaign is already running this
   * adventure in a different one. Left out, the campaign's most recent open play session is used.
   */
  playSessionId?: string
  /** How the turn moves on: only when ended ("ManualOnly", the default) or also after each answered action. */
  turnAdvance?: TurnAdvanceChoice
}

/** How a group run's turn moves on (mirrors the server's TurnAdvanceMode, sent as text). */
export type TurnAdvanceChoice = 'ManualOnly' | 'AfterEachAction'

export interface AdventureSessionDto {
  id: string
  generatedAdventureId: string
  campaignId: string
  /** The play session whose SignalR group receives this run's live events. */
  sessionId: string | null
  characterId: string | null
  currentSceneId: string
  status: AdventureSessionStatus
  outcomeNotes: string | null
  startedAt: string
  completedAt: string | null
  lastActivityAt: string
}

export interface AdventureExitDto {
  sceneId: string
  title: string
  sceneType: string
}

export interface AdventureNpcStateDto {
  npcId: string
  npcName: string
  role: string
  hasBeenMet: boolean
  isAlive: boolean
  revealedInfoIds: string[]
  playerNotes: string | null
}

export interface AdventureRevealedItemDto {
  id: string
  name: string
  description: string
}

export interface AdventureEncounterOutcomeDto {
  encounterId: string
  title: string
  outcome: string
  notes: string | null
}

export interface AdventureSceneDto {
  sceneId: string
  title: string
  description: string
  sceneType: string
  readAloudText: string | null
  objectives: string[]
  exits: AdventureExitDto[]
  npcs: AdventureNpcStateDto[]
}

/** One participant's place in a group run's turn order. */
export interface ParticipantTurnDto {
  participantId: string
  participantName: string
  isActive: boolean
}

export interface AdventureSessionStateDto {
  id: string
  generatedAdventureId: string
  campaignId: string
  sessionId: string | null
  characterId: string | null
  adventureTitle: string
  status: AdventureSessionStatus
  outcomeNotes: string | null
  currentScene: AdventureSceneDto
  currentSceneNumber: number
  totalScenes: number
  completedSceneCount: number
  completedSceneIds: string[]
  npcStates: AdventureNpcStateDto[]
  revealedClues: AdventureRevealedItemDto[]
  revealedSecrets: AdventureRevealedItemDto[]
  encounterOutcomes: AdventureEncounterOutcomeDto[]
  startedAt: string
  completedAt: string | null
  lastActivityAt: string
  /** Group play. False/null/0/[] for a solo run. */
  isGroup: boolean
  activeParticipantId: string | null
  activeParticipantName: string | null
  currentRound: number
  /** In the order players take turns; the array index is the zero-based turn position. */
  turnOrder: ParticipantTurnDto[]
}

/** What happened to one action the AI DM proposed. */
export interface AdventureActionOutcomeDto {
  /** AiActionType name, e.g. "AdvanceScene". */
  actionType: string
  /** "Applied" | "QueuedForApproval" | "ValidationFailed" | "PermissionDenied". */
  outcome: string
  errorMessage: string | null
}

export interface AdventureSessionActionResponse {
  narration: string
  privateDmNote: string | null
  actions: AdventureActionOutcomeDto[]
  state: AdventureSessionStateDto
}

// ---------------------------------------------------------------------------
// SignalR notifications. `sessionId` is the ADVENTURE session id (the run), not the play session.
// ---------------------------------------------------------------------------

export interface AdventureSceneAdvancedEvent {
  sessionId: string
  previousSceneId: string
  newSceneId: string
  narration: string | null
}

export interface AdventureClueRevealedEvent {
  sessionId: string
  clueId: string
  clueName: string
  description: string
}

export interface AdventureSecretRevealedEvent {
  sessionId: string
  secretId: string
  secretName: string
  description: string
}

export interface AdventureNpcUpdatedEvent {
  sessionId: string
  npcId: string
  npcName: string
  hasBeenMet: boolean
  isAlive: boolean
}

export interface AdventureCompletedEvent {
  sessionId: string
  outcomeNotes: string
}

/** The host passed the turn. */
export interface TurnAdvancedEvent {
  sessionId: string
  previousParticipantId: string | null
  activeParticipantId: string
  activeParticipantName: string
  currentRound: number
}

/** A participant was added to the end of a group run's turn order. */
export interface ParticipantJoinedAdventureEvent {
  sessionId: string
  participantId: string
  participantName: string
  /** Zero-based index into the state's turnOrder (0 = first to act): show it plus one. */
  turnPosition: number
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

const BASE = '/api/adventure-sessions'

/** Start a run of an approved adventure, or resume the campaign's in-progress run of it. */
export async function startAdventureSession(
  request: StartAdventureSessionRequest,
): Promise<AdventureSessionDto> {
  const response = await apiClient.post<AdventureSessionDto>(BASE, request)
  return response.data
}

/** Full current state of a run. */
export async function getAdventureSession(id: string): Promise<AdventureSessionStateDto> {
  const response = await apiClient.get<AdventureSessionStateDto>(`${BASE}/${id}`)
  return response.data
}

/** The campaign's in-progress run, or null when there is none. */
export async function getActiveAdventureSession(
  campaignId: string,
): Promise<AdventureSessionDto | null> {
  try {
    const response = await apiClient.get<AdventureSessionDto>(`${BASE}/active`, {
      params: { campaignId },
    })
    return response.data
  } catch (err) {
    if (axios.isAxiosError(err) && err.response?.status === 404) return null
    throw err
  }
}

/** One line of a run's saved conversation. */
export interface AdventureHistoryEntryDto {
  id: string
  entryType: 'PlayerAction' | 'AiNarration'
  content: string
  sceneId: string | null
  createdAt: string
}

/** The run's saved conversation: the last 50 entries, oldest first. */
export async function getAdventureHistory(id: string): Promise<AdventureHistoryEntryDto[]> {
  const response = await apiClient.get<AdventureHistoryEntryDto[]>(`${BASE}/${id}/history`)
  return response.data
}

/** Tell the server a player has come back to this run (clears any stale pending roll). */
export async function resumeAdventureSession(id: string): Promise<void> {
  await apiClient.post(`${BASE}/${id}/resume`)
}

/**
 * A short AI-written "story so far" for a returning player. It is an AI call, so it can take a few
 * seconds: callers show the saved history first and add the recap when it arrives.
 */
export async function getAdventureRecap(id: string): Promise<string> {
  const response = await apiClient.post<{ recap: string }>(`${BASE}/${id}/recap`, null, { timeout: 60_000 })
  return response.data.recap
}

/** Every run that is currently in progress, most recently active first. */
export async function listInProgressAdventureSessions(): Promise<AdventureSessionDto[]> {
  const response = await apiClient.get<AdventureSessionDto[]>(`${BASE}/in-progress`)
  return response.data
}

/** Scenes reachable from the run's current scene. */
export async function getAvailableScenes(id: string): Promise<AdventureExitDto[]> {
  const response = await apiClient.get<AdventureExitDto[]>(`${BASE}/${id}/available-scenes`)
  return response.data
}

/**
 * Submit a free-text player action to the AI DM. The AI can take a while to answer, so this
 * call uses a longer timeout than the shared 30s default.
 */
export async function submitAdventureAction(
  id: string,
  actionText: string,
): Promise<AdventureSessionActionResponse> {
  const response = await apiClient.post<AdventureSessionActionResponse>(
    `${BASE}/${id}/player-action`,
    { actionText },
    { timeout: 180_000 },
  )
  return response.data
}

/** Manually advance to a reachable scene (host override). */
export async function advanceAdventureScene(
  id: string,
  targetSceneId: string,
  transitionNarration?: string | null,
): Promise<AdventureSessionStateDto> {
  const response = await apiClient.post<AdventureSessionStateDto>(`${BASE}/${id}/advance-scene`, {
    targetSceneId,
    transitionNarration: transitionNarration ?? null,
  })
  return response.data
}

/** Abandon the run. */
export async function abandonAdventureSession(id: string): Promise<AdventureSessionStateDto> {
  const response = await apiClient.post<AdventureSessionStateDto>(`${BASE}/${id}/abandon`)
  return response.data
}

/**
 * Start a group run: the listed participants of the play session take turns, in this order. Host only. 201 for a
 * new run, 200 when the campaign is already running this adventure as a group in the same play session (the
 * existing run comes back, with its own turn order), 409 when it is running in another one.
 */
export async function startGroupAdventureSession(
  request: StartGroupAdventureSessionRequest,
): Promise<AdventureSessionStateDto> {
  const response = await apiClient.post<AdventureSessionStateDto>(`${BASE}/start-group`, request)
  return response.data
}

/** Pass the turn of a group run to the next participant (host only). */
export async function advanceAdventureTurn(id: string): Promise<AdventureSessionStateDto> {
  const response = await apiClient.post<AdventureSessionStateDto>(`${BASE}/${id}/advance-turn`)
  return response.data
}

/** Add an approved participant of the run's play session to the end of the turn order (host only). */
export async function addAdventureParticipant(
  id: string,
  participantId: string,
): Promise<AdventureSessionStateDto> {
  const response = await apiClient.post<AdventureSessionStateDto>(`${BASE}/${id}/participants`, {
    participantId,
  })
  return response.data
}

/** Pull the human-readable detail out of a ProblemDetails error response. */
export function adventureErrorMessage(err: unknown, fallback: string): string {
  if (axios.isAxiosError(err)) {
    const detail = (err.response?.data as { detail?: string } | undefined)?.detail
    if (detail) return detail
    if (err.code === 'ECONNABORTED') return 'The AI DM took too long to answer. Try again.'
  }
  if (err instanceof Error && err.message) return err.message
  return fallback
}

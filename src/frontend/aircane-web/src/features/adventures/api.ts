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

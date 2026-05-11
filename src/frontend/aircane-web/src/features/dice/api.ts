import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export enum RollVisibility {
  Public = 0,
  Private = 1,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface RollDto {
  id: string
  sessionId: string
  characterId: string | null
  rollerParticipantId: string
  formula: string | null
  dieResults: number[]
  modifier: number
  total: number
  isManual: boolean
  visibility: RollVisibility
  context: string | null
  createdAt: string
}

export interface RollDiceRequest {
  rollerParticipantId: string
  formula: string
  visibility?: RollVisibility
  characterId?: string | null
  context?: string | null
}

export interface ManualRollRequest {
  rollerParticipantId: string
  dieResults: number[]
  modifier: number
  total: number
  formula?: string | null
  visibility?: RollVisibility
  characterId?: string | null
  context?: string | null
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/**
 * Roll a dice expression within a session.
 * The server parses the formula, generates results, and persists the roll.
 */
export async function rollDice(sessionId: string, request: RollDiceRequest): Promise<RollDto> {
  const response = await apiClient.post<RollDto>(`/api/sessions/${sessionId}/rolls`, request)
  return response.data
}

/**
 * Record a manually entered physical dice roll.
 * The total is treated as authoritative — no server-side re-roll is performed.
 */
export async function recordManualRoll(
  sessionId: string,
  request: ManualRollRequest,
): Promise<RollDto> {
  const response = await apiClient.post<RollDto>(
    `/api/sessions/${sessionId}/manual-rolls`,
    request,
  )
  return response.data
}

/**
 * Fetch the roll log for a session, ordered by most recent first.
 */
export async function getRollLog(
  sessionId: string,
  page = 1,
  pageSize = 50,
): Promise<RollDto[]> {
  const response = await apiClient.get<RollDto[]>(`/api/sessions/${sessionId}/rolls`, {
    params: { page, pageSize },
  })
  return response.data
}

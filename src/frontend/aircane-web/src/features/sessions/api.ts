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

export enum ParticipantRole {
  Player = 0,
  HumanDm = 1,
  Host = 2,
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

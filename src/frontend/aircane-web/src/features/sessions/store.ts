import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
  startSession,
  getSession,
  joinSession,
  getParticipants,
  approveParticipant,
  assignCharacter,
  type SessionDto,
  type StartSessionRequest,
  type JoinSessionRequest,
  type JoinSessionResult,
  type ParticipantDto,
} from './api'
import { clearParticipantSession, saveParticipantSession } from './participantSession'
import { mapJoinError } from './playerErrors'

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return 'An unexpected error occurred.'
}

export const useSessionStore = defineStore('sessions', () => {
  // ── State ──────────────────────────────────────────────────────────────────
  const currentSession = ref<SessionDto | null>(null)
  const participants = ref<ParticipantDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions ────────────────────────────────────────────────────────────────

  /** Start a new session under a campaign. Stores the result (including one-time invite code). */
  async function startSessionAction(
    campaignId: string,
    request: StartSessionRequest,
  ): Promise<SessionDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await startSession(campaignId, request)
      currentSession.value = dto
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Fetch a session by ID. */
  async function fetchSession(sessionId: string): Promise<SessionDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await getSession(sessionId)
      currentSession.value = dto
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /**
   * Join a session with a display name and invite code. Remembers the participant (token, id, name,
   * session) so a reload resumes instead of joining again; see participantSession.ts.
   * Failures are stored as a message a player can act on, not "Request failed with status code 401".
   */
  async function joinSessionAction(
    sessionId: string,
    request: JoinSessionRequest,
  ): Promise<JoinSessionResult> {
    loading.value = true
    error.value = null
    try {
      // A token left over from an earlier visit must not ride along with a fresh join (the API client
      // attaches whatever is stored), and a stale one must never get in its way.
      clearParticipantSession()
      const result = await joinSession(sessionId, request)
      saveParticipantSession({
        sessionId,
        participantId: result.participantId,
        displayName: result.displayName,
        token: result.participantToken,
      })
      return result
    } catch (err) {
      error.value = mapJoinError(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Fetch all participants for a session. */
  async function fetchParticipants(sessionId: string): Promise<ParticipantDto[]> {
    loading.value = true
    error.value = null
    try {
      const list = await getParticipants(sessionId)
      participants.value = list
      return list
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Approve a pending participant. Updates the local participants list. */
  async function approveParticipantAction(
    sessionId: string,
    participantId: string,
  ): Promise<ParticipantDto> {
    loading.value = true
    error.value = null
    try {
      const updated = await approveParticipant(sessionId, participantId)
      const idx = participants.value.findIndex((p) => p.id === participantId)
      if (idx !== -1) {
        participants.value[idx] = updated
      }
      return updated
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Assign a character to a participant. Updates the local participants list. */
  async function assignCharacterAction(
    sessionId: string,
    participantId: string,
    characterId: string,
  ): Promise<ParticipantDto> {
    loading.value = true
    error.value = null
    try {
      const updated = await assignCharacter(sessionId, participantId, characterId)
      const idx = participants.value.findIndex((p) => p.id === participantId)
      if (idx !== -1) {
        participants.value[idx] = updated
      }
      return updated
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Clear the current session state. */
  function clearSession(): void {
    currentSession.value = null
    participants.value = []
    error.value = null
  }

  return {
    // State
    currentSession,
    participants,
    loading,
    error,
    // Actions
    startSession: startSessionAction,
    fetchSession,
    joinSession: joinSessionAction,
    fetchParticipants,
    approveParticipant: approveParticipantAction,
    assignCharacter: assignCharacterAction,
    clearSession,
  }
})

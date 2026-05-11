import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  startSession,
  getSession,
  joinSession,
  endSession,
  getParticipants,
  approveParticipant,
  assignCharacter,
  SessionAccessMode,
  SessionStatus,
  ParticipantRole,
} from '../api'
import apiClient from '@/shared/api/client'

// Mock the shared axios instance so no real HTTP calls are made
vi.mock('@/shared/api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

const mockedGet = vi.mocked(apiClient.get)
const mockedPost = vi.mocked(apiClient.post)

const mockSession = {
  id: 'session-1',
  campaignId: 'campaign-1',
  name: 'Test Session',
  accessMode: SessionAccessMode.LocalLan,
  status: SessionStatus.Active,
  startedAt: '2024-01-01T10:00:00Z',
  endedAt: null,
  createdAt: '2024-01-01T09:00:00Z',
  participantCount: 2,
  inviteCode: 'ABC123',
  joinUrl: 'http://192.168.1.10:5173/join/session-1',
}

describe('sessions API', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ---------------------------------------------------------------------------
  // startSession
  // ---------------------------------------------------------------------------

  describe('startSession', () => {
    it('posts to the correct campaign endpoint and returns the session DTO', async () => {
      mockedPost.mockResolvedValueOnce({ data: mockSession })

      const result = await startSession('campaign-1', {
        name: 'Test Session',
        accessMode: SessionAccessMode.LocalLan,
        requireHostApproval: true,
      })

      expect(mockedPost).toHaveBeenCalledWith('/api/campaigns/campaign-1/start-session', {
        name: 'Test Session',
        accessMode: SessionAccessMode.LocalLan,
        requireHostApproval: true,
      })
      expect(result).toEqual(mockSession)
    })

    it('propagates errors when the request fails', async () => {
      mockedPost.mockRejectedValueOnce(new Error('Network Error'))

      await expect(
        startSession('campaign-1', { name: 'Test', accessMode: SessionAccessMode.Solo }),
      ).rejects.toThrow('Network Error')
    })
  })

  // ---------------------------------------------------------------------------
  // getSession
  // ---------------------------------------------------------------------------

  describe('getSession', () => {
    it('fetches the session by ID', async () => {
      const sessionWithoutCode = { ...mockSession, inviteCode: null, joinUrl: null }
      mockedGet.mockResolvedValueOnce({ data: sessionWithoutCode })

      const result = await getSession('session-1')

      expect(mockedGet).toHaveBeenCalledWith('/api/sessions/session-1')
      expect(result.id).toBe('session-1')
      expect(result.inviteCode).toBeNull()
    })

    it('propagates errors when the session is not found', async () => {
      mockedGet.mockRejectedValueOnce(new Error('Not Found'))

      await expect(getSession('nonexistent')).rejects.toThrow('Not Found')
    })
  })

  // ---------------------------------------------------------------------------
  // joinSession
  // ---------------------------------------------------------------------------

  describe('joinSession', () => {
    it('posts to the join endpoint with display name and invite code', async () => {
      const joinResult = {
        participantId: 'participant-1',
        displayName: 'Thorin',
        role: 0,
        isApproved: true,
        participantToken: 'signed.jwt.token',
      }
      mockedPost.mockResolvedValueOnce({ data: joinResult })

      const result = await joinSession('session-1', {
        displayName: 'Thorin',
        inviteCode: 'ABC123',
      })

      expect(mockedPost).toHaveBeenCalledWith('/api/sessions/session-1/join', {
        displayName: 'Thorin',
        inviteCode: 'ABC123',
      })
      expect(result.participantToken).toBe('signed.jwt.token')
      expect(result.displayName).toBe('Thorin')
    })

    it('propagates errors when the invite code is wrong', async () => {
      mockedPost.mockRejectedValueOnce(new Error('Invalid invite code'))

      await expect(
        joinSession('session-1', { displayName: 'Thorin', inviteCode: 'WRONG' }),
      ).rejects.toThrow('Invalid invite code')
    })
  })

  // ---------------------------------------------------------------------------
  // endSession
  // ---------------------------------------------------------------------------

  describe('endSession', () => {
    it('posts to the end endpoint with an optional summary', async () => {
      mockedPost.mockResolvedValueOnce({ data: {} })

      await endSession('session-1', 'Great session!')

      expect(mockedPost).toHaveBeenCalledWith('/api/sessions/session-1/end', {
        summary: 'Great session!',
      })
    })

    it('sends null summary when none is provided', async () => {
      mockedPost.mockResolvedValueOnce({ data: {} })

      await endSession('session-1')

      expect(mockedPost).toHaveBeenCalledWith('/api/sessions/session-1/end', {
        summary: null,
      })
    })
  })

  // ---------------------------------------------------------------------------
  // getParticipants
  // ---------------------------------------------------------------------------

  describe('getParticipants', () => {
    it('fetches participants for a session', async () => {
      const mockParticipants = [
        {
          id: 'participant-1',
          sessionId: 'session-1',
          displayName: 'Thorin',
          role: ParticipantRole.Player,
          characterId: null,
          isApproved: false,
          joinedAt: '2024-01-01T10:00:00Z',
          lastSeenAt: '2024-01-01T10:00:00Z',
        },
      ]
      mockedGet.mockResolvedValueOnce({ data: mockParticipants })

      const result = await getParticipants('session-1')

      expect(mockedGet).toHaveBeenCalledWith('/api/sessions/session-1/participants')
      expect(result).toHaveLength(1)
      expect(result[0].displayName).toBe('Thorin')
    })

    it('returns an empty array when no participants have joined', async () => {
      mockedGet.mockResolvedValueOnce({ data: [] })

      const result = await getParticipants('session-1')

      expect(result).toEqual([])
    })

    it('propagates errors when the session is not found', async () => {
      mockedGet.mockRejectedValueOnce(new Error('Not Found'))

      await expect(getParticipants('nonexistent')).rejects.toThrow('Not Found')
    })
  })

  // ---------------------------------------------------------------------------
  // approveParticipant
  // ---------------------------------------------------------------------------

  describe('approveParticipant', () => {
    it('posts to the approve-participant endpoint with the participant ID', async () => {
      const approvedParticipant = {
        id: 'participant-1',
        sessionId: 'session-1',
        displayName: 'Thorin',
        role: ParticipantRole.Player,
        characterId: null,
        isApproved: true,
        joinedAt: '2024-01-01T10:00:00Z',
        lastSeenAt: '2024-01-01T10:00:00Z',
      }
      mockedPost.mockResolvedValueOnce({ data: approvedParticipant })

      const result = await approveParticipant('session-1', 'participant-1')

      expect(mockedPost).toHaveBeenCalledWith('/api/sessions/session-1/approve-participant', {
        participantId: 'participant-1',
      })
      expect(result.isApproved).toBe(true)
    })

    it('propagates errors when the participant is not found', async () => {
      mockedPost.mockRejectedValueOnce(new Error('Not Found'))

      await expect(approveParticipant('session-1', 'nonexistent')).rejects.toThrow('Not Found')
    })
  })

  // ---------------------------------------------------------------------------
  // assignCharacter
  // ---------------------------------------------------------------------------

  describe('assignCharacter', () => {
    it('posts to the assign-character endpoint with participant and character IDs', async () => {
      const updatedParticipant = {
        id: 'participant-1',
        sessionId: 'session-1',
        displayName: 'Thorin',
        role: ParticipantRole.Player,
        characterId: 'char-1',
        isApproved: true,
        joinedAt: '2024-01-01T10:00:00Z',
        lastSeenAt: '2024-01-01T10:00:00Z',
      }
      mockedPost.mockResolvedValueOnce({ data: updatedParticipant })

      const result = await assignCharacter('session-1', 'participant-1', 'char-1')

      expect(mockedPost).toHaveBeenCalledWith('/api/sessions/session-1/assign-character', {
        participantId: 'participant-1',
        characterId: 'char-1',
      })
      expect(result.characterId).toBe('char-1')
    })

    it('propagates errors when the participant is not found', async () => {
      mockedPost.mockRejectedValueOnce(new Error('Not Found'))

      await expect(assignCharacter('session-1', 'nonexistent', 'char-1')).rejects.toThrow(
        'Not Found',
      )
    })
  })
})

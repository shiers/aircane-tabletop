import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { AxiosError, type AxiosResponse } from 'axios'
import GroupLobbyPanel from '../components/GroupLobbyPanel.vue'
import { ParticipantRole, SessionAccessMode, SessionStatus, type ParticipantDto, type SessionDto } from '../api'
import * as adventuresApi from '@/features/adventures/api'
import { groupRunState } from '@/features/adventures/__tests__/fixtures'

vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

vi.mock('@/features/adventures/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/adventures/api')>()
  return { ...actual, startGroupAdventureSession: vi.fn() }
})

const startGroup = vi.mocked(adventuresApi.startGroupAdventureSession)

const SESSION: SessionDto = {
  id: 'play-1',
  campaignId: 'campaign-1',
  name: 'Group: The Sunken Archive',
  accessMode: SessionAccessMode.LocalLan,
  status: SessionStatus.Active,
  startedAt: null,
  endedAt: null,
  createdAt: '2026-10-01T00:00:00Z',
  participantCount: 0,
  inviteCode: null,
  joinUrl: null,
}

function person(id: string, overrides: Partial<ParticipantDto> = {}): ParticipantDto {
  return {
    id,
    sessionId: 'play-1',
    displayName: id,
    role: ParticipantRole.Player,
    characterId: null,
    isApproved: true,
    joinedAt: '2026-10-01T10:00:00Z',
    lastSeenAt: '2026-10-01T10:00:00Z',
    ...overrides,
  }
}

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

function mountLobby(participants: ParticipantDto[], extra: Record<string, unknown> = {}) {
  return mount(GroupLobbyPanel, {
    props: {
      sessionId: 'play-1',
      campaignId: 'campaign-1',
      adventureId: 'adventure-1',
      adventureTitle: 'The Sunken Archive',
      session: SESSION,
      inviteCode: 'ABCD1234',
      joinLink: { url: 'http://192.168.1.42:5173/join/play-1', reachableFromLan: true },
      participants,
      availableCharacters: [],
      ...extra,
    },
  })
}

const orderNames = (wrapper: ReturnType<typeof mountLobby>) =>
  wrapper.findAll('[data-testid="turn-order-item"]').map((li) => li.find('.truncate').text())

beforeEach(() => {
  vi.clearAllMocks()
})

describe('GroupLobbyPanel', () => {
  describe('invite', () => {
    it('shows the invite code and the LAN join link', () => {
      const wrapper = mountLobby([])

      expect(wrapper.text()).toContain('ABCD1234')
      expect(wrapper.text()).toContain('http://192.168.1.42:5173/join/play-1')
      expect(wrapper.text()).not.toContain('localhost')
    })

    it('explains a lost invite code instead of showing an empty box', () => {
      const wrapper = mountLobby([], { inviteCode: null })

      expect(wrapper.find('[data-testid="no-invite-code"]').text()).toMatch(/only once/i)
      expect(wrapper.text()).not.toContain('ABCD1234')
    })

    it('warns when the link only works on the host computer', () => {
      const wrapper = mountLobby([], { joinLink: { url: 'http://localhost:5173/join/play-1', reachableFromLan: false } })

      expect(wrapper.find('[data-testid="no-lan-address"]').exists()).toBe(true)
    })
  })

  describe('Start adventure enabled / disabled', () => {
    it('is disabled with nobody approved, and says why', () => {
      const wrapper = mountLobby([person('waiting', { isApproved: false })])

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="start-reason"]').text()).toMatch(/approve at least one player/i)
    })

    it('is disabled for an empty lobby', () => {
      const wrapper = mountLobby([])

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
    })

    it('is enabled once one player is approved', () => {
      const wrapper = mountLobby([person('alice')])

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeUndefined()
      expect(wrapper.find('[data-testid="start-reason"]').exists()).toBe(false)
    })

    it('becomes enabled when a player is approved and the list refreshes', async () => {
      const wrapper = mountLobby([person('alice', { isApproved: false })])
      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()

      await wrapper.setProps({ participants: [person('alice')] })

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeUndefined()
    })

    it('is not enabled by approved people who are not players', () => {
      const wrapper = mountLobby([person('spectator', { role: ParticipantRole.Spectator })])

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
    })

    it('is disabled, with the reason, when the campaign already has a solo adventure', () => {
      const wrapper = mountLobby([person('alice')], { runBlockedReason: 'A solo adventure is in progress.' })

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="start-reason"]').text()).toBe('A solo adventure is in progress.')
    })

    it('is disabled once the session has ended', () => {
      const wrapper = mountLobby([person('alice')], { session: { ...SESSION, status: SessionStatus.Ended } })

      expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
    })
  })

  describe('turn order', () => {
    it('defaults to the order players were approved in', async () => {
      const wrapper = mountLobby([])
      await wrapper.setProps({ participants: [person('alice')] })
      await wrapper.setProps({ participants: [person('alice'), person('bob')] })
      await wrapper.setProps({ participants: [person('alice'), person('bob'), person('cara')] })

      expect(orderNames(wrapper)).toEqual(['alice', 'bob', 'cara'])
    })

    it('moves a player up and down, and disables the arrows at the ends', async () => {
      const wrapper = mountLobby([person('alice'), person('bob'), person('cara')])

      await wrapper.findAll('[data-testid="move-down"]')[0].trigger('click') // alice down
      expect(orderNames(wrapper)).toEqual(['bob', 'alice', 'cara'])

      await wrapper.findAll('[data-testid="move-up"]')[2].trigger('click') // cara up
      expect(orderNames(wrapper)).toEqual(['bob', 'cara', 'alice'])

      expect(wrapper.findAll('[data-testid="move-up"]')[0].attributes('disabled')).toBeDefined()
      expect(wrapper.findAll('[data-testid="move-down"]')[2].attributes('disabled')).toBeDefined()
    })

    it('keeps a manual order when the list refreshes', async () => {
      const wrapper = mountLobby([person('alice'), person('bob')])
      await wrapper.findAll('[data-testid="move-down"]')[0].trigger('click')

      await wrapper.setProps({ participants: [person('alice'), person('bob'), person('cara')] })

      expect(orderNames(wrapper)).toEqual(['bob', 'alice', 'cara'])
    })

    it('gives every arrow button an accessible name', () => {
      const wrapper = mountLobby([person('alice'), person('bob')])

      expect(wrapper.find('[aria-label="Move alice down"]').exists()).toBe(true)
      expect(wrapper.find('[aria-label="Move bob up"]').exists()).toBe(true)
    })

    it('shows the character a player will be, when assigned', () => {
      const wrapper = mountLobby([person('alice', { characterId: 'char-1' })], {
        availableCharacters: [{ id: 'char-1', name: 'Mira' }],
      })

      expect(wrapper.find('[data-testid="turn-order-item"]').text()).toContain('as Mira')
    })

    it('tells the host they have no seat, and how to get one', () => {
      const wrapper = mountLobby([])

      expect(wrapper.find('[data-testid="host-seat-hint"]').text()).toMatch(/don't take a turn/i)
      expect(wrapper.find('[data-testid="host-seat-hint"]').text()).toMatch(/join as a player from another browser/i)
    })
  })

  describe('requests', () => {
    it('shows a pending request and emits approve', async () => {
      const wrapper = mountLobby([person('newcomer', { isApproved: false })])

      await wrapper.find('[aria-label="Approve newcomer"]').trigger('click')

      expect(wrapper.emitted('approve')).toEqual([['newcomer']])
    })

    it('dismisses a request on this screen only', async () => {
      const wrapper = mountLobby([person('newcomer', { isApproved: false })])

      await wrapper.find('[data-testid="dismiss-participant"]').trigger('click')

      expect(wrapper.find('[aria-label="Approve newcomer"]').exists()).toBe(false)
      expect(wrapper.emitted('approve')).toBeUndefined()
    })
  })

  describe('starting', () => {
    it('posts the approved players in the displayed order and emits the new run', async () => {
      const run = groupRunState({ sessionId: 'play-1' })
      startGroup.mockResolvedValue(run)
      const wrapper = mountLobby([person('alice'), person('bob'), person('waiting', { isApproved: false })])
      await wrapper.findAll('[data-testid="move-down"]')[0].trigger('click') // bob, alice

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(startGroup).toHaveBeenCalledWith({
        generatedAdventureId: 'adventure-1',
        campaignId: 'campaign-1',
        participantIds: ['bob', 'alice'],
      })
      expect(wrapper.emitted('started')).toEqual([[run]])
    })

    it('does nothing when started while disabled', async () => {
      const wrapper = mountLobby([])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')

      expect(startGroup).not.toHaveBeenCalled()
    })

    it('shows the host-only message for a 403', async () => {
      startGroup.mockRejectedValue(httpError(403))
      const wrapper = mountLobby([person('alice')])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="start-error"]').text()).toMatch(/only the host/i)
      expect(wrapper.emitted('started')).toBeUndefined()
    })

    it('shows the server message for a 409', async () => {
      const detail = 'This campaign already has an adventure in progress. Abandon or complete it before starting another.'
      startGroup.mockRejectedValue(httpError(409, { detail }))
      const wrapper = mountLobby([person('alice')])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="start-error"]').text()).toBe(detail)
    })

    it('shows a 400 message and asks the parent to refresh the player list', async () => {
      startGroup.mockRejectedValue(
        httpError(400, { detail: "Not active participants of the campaign's current session: 3f2504e0-4f89-11d3-9a0c-0305e82c3301." }),
      )
      const wrapper = mountLobby([person('alice')])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="start-error"]').text()).toMatch(/no longer an approved player/i)
      expect(wrapper.emitted('refresh')).toHaveLength(1)
    })

    it('can try again after an error', async () => {
      startGroup.mockRejectedValueOnce(httpError(500)).mockResolvedValueOnce(groupRunState({ sessionId: 'play-1' }))
      const wrapper = mountLobby([person('alice')])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="start-error"]').exists()).toBe(true)

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="start-error"]').exists()).toBe(false)
      expect(wrapper.emitted('started')).toHaveLength(1)
    })

    // start-group takes no session id and may hand back an existing run of this adventure that
    // belongs to another play session; the players waiting in this lobby would not be in it.
    it('does not open a run that belongs to a different play session', async () => {
      startGroup.mockResolvedValue(groupRunState({ sessionId: 'some-other-session' }))
      const wrapper = mountLobby([person('alice')])

      await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.emitted('started')).toBeUndefined()
      expect(wrapper.find('[data-testid="start-error"]').text()).toMatch(/different play session/i)
    })
  })
})

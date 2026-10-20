import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ParticipantApprovalPanel from '../ParticipantApprovalPanel.vue'
import GroupLobbyPanel from '../components/GroupLobbyPanel.vue'
import InvitePlayersPanel from '@/features/adventures/components/InvitePlayersPanel.vue'
import * as sessionsApi from '../api'
import { ParticipantRole, SessionAccessMode, SessionStatus, type ParticipantDto, type SessionDto } from '../api'
import { useSessionStore } from '../store'

/**
 * The host's side of a returning player: a waiting request with the same name as someone already in the session
 * is flagged ("Already in this session"), and the host can give that participant's seat to it after confirming.
 */

vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return { ...actual, replaceParticipant: vi.fn() }
})

const mockedReplace = vi.mocked(sessionsApi.replaceParticipant)

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

const ghost = person('old-bifur', { displayName: 'Bifur', characterId: 'char-1' })
const back = person('new-bifur', { displayName: 'bifur', isApproved: false, joinedAt: '2026-10-01T11:00:00Z', sameNameAsParticipantId: 'old-bifur' })
const stranger = person('nori', { displayName: 'Nori', isApproved: false })

function mountPanel(participants: ParticipantDto[]) {
  return mount(ParticipantApprovalPanel, {
    props: { sessionId: 'play-1', participants, availableCharacters: [] },
  })
}

describe('ParticipantApprovalPanel: a request from someone already in the session', () => {
  it('flags only the matching request, and keeps Approve next to it', () => {
    const wrapper = mountPanel([ghost, back, stranger])

    const badges = wrapper.findAll('[data-testid="same-name-badge"]')
    expect(badges).toHaveLength(1)
    expect(badges[0].text()).toBe('Already in this session')
    expect(wrapper.find('[aria-label="Approve bifur"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Approve Nori"]').exists()).toBe(true)
  })

  it('names the participant it would replace and asks before doing anything', async () => {
    const wrapper = mountPanel([ghost, back])

    await wrapper.find('[data-testid="replace-seat"]').trigger('click')

    const confirm = wrapper.find('[data-testid="replace-seat-confirm"]')
    expect(confirm.exists()).toBe(true)
    expect(confirm.find('[data-testid="replace-seat-target"]').text()).toBe('Bifur')
    expect(confirm.text()).toContain('signed out')
    expect(wrapper.emitted('replace')).toBeUndefined()

    await wrapper.find('[data-testid="confirm-replace"]').trigger('click')

    expect(wrapper.emitted('replace')).toEqual([['new-bifur', 'old-bifur']])
  })

  it('does nothing when the host cancels', async () => {
    const wrapper = mountPanel([ghost, back])

    await wrapper.find('[data-testid="replace-seat"]').trigger('click')
    await wrapper.find('[data-testid="cancel-replace"]').trigger('click')

    expect(wrapper.find('[data-testid="replace-seat-confirm"]').exists()).toBe(false)
    expect(wrapper.emitted('replace')).toBeUndefined()
  })

  it('offers no Replace when the match has no seat yet (two waiting requests), only the badge', () => {
    const first = person('first', { displayName: 'Bifur', isApproved: false })
    const second = person('second', { displayName: 'Bifur', isApproved: false, sameNameAsParticipantId: 'first' })

    const wrapper = mountPanel([first, second])

    expect(wrapper.find('[data-testid="same-name-badge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="replace-seat"]').exists()).toBe(false)
  })
})

describe('InvitePlayersPanel: the same flag while an adventure is running', () => {
  it('shows the badge on a flagged request and passes the replace up', async () => {
    const wrapper = mount(InvitePlayersPanel, {
      props: {
        inviteCode: 'ABCD1234',
        joinLink: { url: 'http://192.168.1.42:5173/join/play-1', reachableFromLan: true },
        pending: [back],
        latePlayers: [],
        roster: [ghost, back],
      },
    })

    expect(wrapper.find('[data-testid="same-name-badge"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="invite-approve"]').exists()).toBe(true)
    await wrapper.find('[data-testid="replace-seat"]').trigger('click')
    expect(wrapper.find('[data-testid="replace-seat-target"]').text()).toBe('Bifur')
    await wrapper.find('[data-testid="confirm-replace"]').trigger('click')

    expect(wrapper.emitted('replace')).toEqual([['new-bifur', 'old-bifur']])
  })
})

describe('GroupLobbyPanel: a replaced seat keeps its place in the turn order being built', () => {
  const SESSION: SessionDto = {
    id: 'play-1', campaignId: 'campaign-1', name: 'Group', accessMode: SessionAccessMode.LocalLan,
    status: SessionStatus.Active, startedAt: null, endedAt: null, createdAt: '2026-10-01T00:00:00Z',
    participantCount: 0, inviteCode: null, joinUrl: null,
  }

  it('puts the new participant where the old one was', async () => {
    const alice = person('alice', { displayName: 'Alice', joinedAt: '2026-10-01T09:00:00Z' })
    const cara = person('cara', { displayName: 'Cara', joinedAt: '2026-10-01T10:30:00Z' })
    const wrapper = mount(GroupLobbyPanel, {
      props: {
        sessionId: 'play-1', campaignId: 'campaign-1', adventureId: 'adventure-1', session: SESSION,
        inviteCode: 'ABCD1234', joinLink: { url: 'http://x/join/play-1', reachableFromLan: true },
        participants: [alice, ghost, cara, back], availableCharacters: [],
      },
    })
    const order = () => wrapper.findAll('[data-testid="turn-order-item"]').map((li) => li.find('.truncate').text())
    expect(order()).toEqual(['Alice', 'Bifur', 'Cara'])

    await wrapper.find('[data-testid="replace-seat"]').trigger('click')
    await wrapper.find('[data-testid="confirm-replace"]').trigger('click')
    expect(wrapper.emitted('replace')).toEqual([['new-bifur', 'old-bifur']])

    // The parent re-reads the roster: the old participant is gone, the new one approved.
    await wrapper.setProps({ participants: [alice, cara, { ...back, isApproved: true, sameNameAsParticipantId: null }] })

    expect(order()).toEqual(['Alice', 'bifur', 'Cara'])
  })
})

describe('the session store: replace seat', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('calls the server and swaps the participants in the list', async () => {
    const approved = { ...back, isApproved: true, characterId: 'char-1', sameNameAsParticipantId: null }
    mockedReplace.mockResolvedValue({ participant: approved, replacedParticipantId: 'old-bifur' })
    const store = useSessionStore()
    store.participants = [ghost, back, stranger]

    await store.replaceParticipant('play-1', 'old-bifur', 'new-bifur')
    await flushPromises()

    expect(mockedReplace).toHaveBeenCalledWith('play-1', 'old-bifur', 'new-bifur')
    expect(store.participants.map((p) => [p.id, p.isApproved, p.characterId])).toEqual([
      ['new-bifur', true, 'char-1'],
      ['nori', false, null],
    ])
  })
})

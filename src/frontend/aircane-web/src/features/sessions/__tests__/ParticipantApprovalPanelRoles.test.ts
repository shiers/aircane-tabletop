import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ParticipantApprovalPanel from '../ParticipantApprovalPanel.vue'
import { ParticipantRole, type ParticipantDto } from '../api'

function person(id: string, overrides: Partial<ParticipantDto> = {}): ParticipantDto {
  return {
    id,
    sessionId: 'session-1',
    displayName: id,
    role: ParticipantRole.Player,
    characterId: null,
    isApproved: false,
    joinedAt: '2024-01-01T10:00:00Z',
    lastSeenAt: '2024-01-01T10:00:00Z',
    ...overrides,
  }
}

function mountPanel(participants: ParticipantDto[]) {
  return mount(ParticipantApprovalPanel, {
    props: { sessionId: 'session-1', participants, availableCharacters: [] },
  })
}

describe('ParticipantApprovalPanel: role labels', () => {
  // The API sends the role as a number (Host 0, HumanDm 1, Player 2, Spectator 3). The panel used to
  // read 2 as "Host", so every player a joiner created showed up labelled Host.
  it.each([
    [ParticipantRole.Host, 'Host'],
    [ParticipantRole.HumanDm, 'Human DM'],
    [ParticipantRole.Player, 'Player'],
    [ParticipantRole.Spectator, 'Spectator'],
  ])('labels role %i as %s', (role, label) => {
    const wrapper = mountPanel([person('someone', { role })])

    expect(wrapper.find('li').text()).toContain(label)
  })

  it('labels the numeric role the server sends for a joining player (2) as Player', () => {
    const wrapper = mountPanel([person('joiner', { role: 2 as ParticipantRole })])

    expect(wrapper.find('li').text()).toContain('Player')
    expect(wrapper.find('li').text()).not.toContain('Host')
  })
})

describe('ParticipantApprovalPanel: deny, remove and presence', () => {
  it('offers Deny on a pending request, which asks the parent (it never approves)', async () => {
    const wrapper = mountPanel([person('newcomer')])

    await wrapper.find('[data-testid="deny-participant"]').trigger('click')

    expect(wrapper.emitted('deny')).toEqual([['newcomer']])
    expect(wrapper.emitted('approve')).toBeUndefined()
    expect(wrapper.find('[data-testid="dismiss-participant"]').exists()).toBe(false)
  })

  it('says what denying does', () => {
    const wrapper = mountPanel([person('newcomer')])

    expect(wrapper.find('[data-testid="deny-participant"]').attributes('title')).toMatch(/declines the request/i)
  })

  it('offers Remove, not Deny, for an approved player', async () => {
    const wrapper = mountPanel([person('done', { isApproved: true })])

    expect(wrapper.find('[data-testid="deny-participant"]').exists()).toBe(false)
    await wrapper.find('[data-testid="remove-participant"]').trigger('click')
    expect(wrapper.emitted('remove')).toEqual([['done']])
  })

  it('never offers to remove the host', () => {
    const wrapper = mountPanel([person('host', { isApproved: true, role: ParticipantRole.Host })])

    expect(wrapper.find('[data-testid="remove-participant"]').exists()).toBe(false)
  })

  it('shows connected or away when the roster says, and nothing when it does not', () => {
    const wrapper = mountPanel([
      person('here', { isApproved: true, isConnected: true }),
      person('gone', { isApproved: true, isConnected: false }),
      person('unknown', { isApproved: true }),
    ])

    const dots = wrapper.findAll('[data-testid="presence-dot"]').map((d) => d.attributes('aria-label'))
    expect(dots).toEqual(['Connected', 'Away'])
  })
})

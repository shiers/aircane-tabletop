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

function mountPanel(participants: ParticipantDto[], dismissible?: boolean) {
  return mount(ParticipantApprovalPanel, {
    props: { sessionId: 'session-1', participants, availableCharacters: [], dismissible },
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

describe('ParticipantApprovalPanel: dismiss', () => {
  it('has no Dismiss button unless asked for', () => {
    const wrapper = mountPanel([person('newcomer')])

    expect(wrapper.find('[data-testid="dismiss-participant"]').exists()).toBe(false)
  })

  it('emits dismiss for a pending request, without approving', async () => {
    const wrapper = mountPanel([person('newcomer')], true)

    await wrapper.find('[data-testid="dismiss-participant"]').trigger('click')

    expect(wrapper.emitted('dismiss')).toEqual([['newcomer']])
    expect(wrapper.emitted('approve')).toBeUndefined()
  })

  it('does not offer Dismiss for someone already approved', () => {
    const wrapper = mountPanel([person('done', { isApproved: true })], true)

    expect(wrapper.find('[data-testid="dismiss-participant"]').exists()).toBe(false)
  })

  it('says what dismissing does', () => {
    const wrapper = mountPanel([person('newcomer')], true)

    expect(wrapper.find('[data-testid="dismiss-participant"]').attributes('title')).toMatch(/stay unapproved/i)
  })
})

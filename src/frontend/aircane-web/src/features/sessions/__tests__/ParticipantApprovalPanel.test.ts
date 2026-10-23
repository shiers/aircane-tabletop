import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ParticipantApprovalPanel from '../ParticipantApprovalPanel.vue'
import { ParticipantRole, type ParticipantDto } from '../api'
import { features } from '@/shared/features'

const mockPendingParticipant: ParticipantDto = {
  id: 'participant-1',
  sessionId: 'session-1',
  displayName: 'Thorin',
  role: ParticipantRole.Player,
  characterId: null,
  isApproved: false,
  joinedAt: '2024-01-01T10:00:00Z',
  lastSeenAt: '2024-01-01T10:00:00Z',
}

const mockApprovedParticipant: ParticipantDto = {
  id: 'participant-2',
  sessionId: 'session-1',
  displayName: 'Gandalf',
  role: ParticipantRole.Player,
  characterId: null,
  isApproved: true,
  joinedAt: '2024-01-01T10:01:00Z',
  lastSeenAt: '2024-01-01T10:01:00Z',
}

const mockCharacters = [
  { id: 'char-1', name: 'Thorin Oakenshield' },
  { id: 'char-2', name: 'Gandalf the Grey' },
]

function mountPanel(overrides: Partial<{
  participants: ParticipantDto[]
  availableCharacters: { id: string; name: string }[]
  loading: boolean
}> = {}) {
  return mount(ParticipantApprovalPanel, {
    props: {
      sessionId: 'session-1',
      participants: [mockPendingParticipant, mockApprovedParticipant],
      availableCharacters: mockCharacters,
      loading: false,
      ...overrides,
    },
  })
}

describe('ParticipantApprovalPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ---------------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------------

  it('renders the panel heading', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Participants')
  })

  it('shows pending participants in the awaiting approval section', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Awaiting Approval')
    expect(wrapper.text()).toContain('Thorin')
  })

  it('shows approved participants in the approved section', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Approved')
    expect(wrapper.text()).toContain('Gandalf')
  })

  it('shows empty state when no participants have joined', () => {
    const wrapper = mountPanel({ participants: [] })
    expect(wrapper.text()).toContain('No participants have joined yet.')
  })

  it('shows the pending count in the section heading', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Awaiting Approval (1)')
  })

  it('shows the approved count in the section heading', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Approved (1)')
  })

  it('renders an Approve button for each pending participant', () => {
    const wrapper = mountPanel()
    const approveButton = wrapper.find('[aria-label="Approve Thorin"]')
    expect(approveButton.exists()).toBe(true)
  })

  it('does not render an Approve button for approved participants', () => {
    const wrapper = mountPanel()
    const approveButton = wrapper.find('[aria-label="Approve Gandalf"]')
    expect(approveButton.exists()).toBe(false)
  })

  it('renders a character select for approved participants when characters are available', () => {
    const wrapper = mountPanel()
    const select = wrapper.find('[aria-label="Assign character to Gandalf"]')
    expect(select.exists()).toBe(true)
  })

  it('shows available characters in the select dropdown', () => {
    const wrapper = mountPanel()
    const select = wrapper.find('[aria-label="Assign character to Gandalf"]')
    expect(select.text()).toContain('Thorin Oakenshield')
    expect(select.text()).toContain('Gandalf the Grey')
  })

  it('shows a dash when no character is assigned and no characters are available', () => {
    const wrapper = mountPanel({ availableCharacters: [] })
    // Gandalf has no character assigned and no characters available
    expect(wrapper.text()).toContain('-')
  })

  // ---------------------------------------------------------------------------
  // Interactions
  // ---------------------------------------------------------------------------

  it('emits approve event with the participant ID when Approve is clicked', async () => {
    const wrapper = mountPanel()
    const approveButton = wrapper.find('[aria-label="Approve Thorin"]')
    await approveButton.trigger('click')

    expect(wrapper.emitted('approve')).toBeTruthy()
    expect(wrapper.emitted('approve')![0]).toEqual(['participant-1'])
  })

  it('emits assign-character event when a character is selected', async () => {
    const wrapper = mountPanel()
    const select = wrapper.find('[aria-label="Assign character to Gandalf"]') as ReturnType<typeof wrapper.find>
    await select.setValue('char-1')
    await select.trigger('change')

    expect(wrapper.emitted('assign-character')).toBeTruthy()
    expect(wrapper.emitted('assign-character')![0]).toEqual(['participant-2', 'char-1'])
  })

  it('disables the Approve button when loading is true', () => {
    const wrapper = mountPanel({ loading: true })
    const approveButton = wrapper.find('[aria-label="Approve Thorin"]')
    expect(approveButton.attributes('disabled')).toBeDefined()
  })

  it('disables the character select when loading is true', () => {
    const wrapper = mountPanel({ loading: true })
    const select = wrapper.find('[aria-label="Assign character to Gandalf"]')
    expect(select.attributes('disabled')).toBeDefined()
  })

  // ---------------------------------------------------------------------------
  // Role labels
  // ---------------------------------------------------------------------------

  it('displays the Player role label', () => {
    const wrapper = mountPanel()
    // Both participants are Players
    const playerLabels = wrapper.findAll('p.text-xs.text-gray-400')
    expect(playerLabels.some((el) => el.text() === 'Player')).toBe(true)
  })
})

describe('ParticipantApprovalPanel and the Characters feature flag', () => {
  afterEach(() => {
    features.characters = true
  })

  it.each([
    [true, true],
    [false, false],
  ])('with the feature %s, the Character label and assign dropdown are shown: %s', (on, shown) => {
    features.characters = on

    const wrapper = mountPanel({ participants: [mockApprovedParticipant] })

    expect(wrapper.text().includes('Character:')).toBe(shown)
    expect(wrapper.find('select').exists()).toBe(shown)
    expect(wrapper.find('[aria-label="Assign character to Gandalf"]').exists()).toBe(shown)
  })

  it('with the feature off, shows no character at all, even for someone who has one, but keeps the person and Remove', () => {
    features.characters = false
    const holder = { ...mockApprovedParticipant, characterId: 'char-2' }

    const wrapper = mountPanel({ participants: [holder], availableCharacters: mockCharacters })

    expect(wrapper.text()).toContain('Gandalf')
    expect(wrapper.text()).not.toContain('Gandalf the Grey')
    expect(wrapper.text()).not.toMatch(/character/i)
    expect(wrapper.find('[data-testid="remove-participant"]').exists()).toBe(true)
  })

  it('with the feature off and no characters to offer, there is no dash standing in for a character either', () => {
    features.characters = false

    const wrapper = mountPanel({ participants: [mockApprovedParticipant], availableCharacters: [] })

    expect(wrapper.text()).not.toMatch(/character/i)
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import PlayerSessionView from '../PlayerSessionView.vue'
import { SessionAccessMode, SessionStatus, ParticipantRole } from '../api'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn().mockReturnValue({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
    build: vi.fn().mockReturnValue({
      on: vi.fn(),
      start: vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
    }),
  }),
  LogLevel: { Warning: 1 },
}))

vi.mock('../../dice/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../dice/api')>()
  return {
    ...actual,
    rollDice: vi.fn().mockResolvedValue({
      id: 'roll-1',
      sessionId: 'session-1',
      characterId: null,
      rollerParticipantId: 'participant-1',
      formula: '1d20',
      dieResults: [15],
      modifier: 0,
      total: 15,
      isManual: false,
      visibility: 0,
      context: null,
      createdAt: '2024-01-01T10:00:00Z',
    }),
    recordManualRoll: vi.fn().mockResolvedValue({
      id: 'roll-2',
      sessionId: 'session-1',
      characterId: null,
      rollerParticipantId: 'participant-1',
      formula: null,
      dieResults: [12],
      modifier: 0,
      total: 12,
      isManual: true,
      visibility: 0,
      context: null,
      createdAt: '2024-01-01T10:01:00Z',
    }),
  }
})

// Mock session store - player with no character assigned
const mockStoreNoCharacter = {
  currentSession: {
    id: 'session-1',
    campaignId: 'campaign-1',
    name: 'Friday Night Game',
    accessMode: SessionAccessMode.LocalLan,
    status: SessionStatus.Active,
    startedAt: '2024-01-01T10:00:00Z',
    endedAt: null,
    createdAt: '2024-01-01T09:00:00Z',
    participantCount: 1,
    inviteCode: null,
    joinUrl: null,
  },
  participants: [
    {
      id: 'participant-1',
      sessionId: 'session-1',
      displayName: 'Thorin',
      role: ParticipantRole.Player,
      characterId: null,
      isApproved: true,
      joinedAt: '2024-01-01T10:00:00Z',
      lastSeenAt: '2024-01-01T10:00:00Z',
    },
  ],
  loading: false,
  error: null,
  fetchSession: vi.fn().mockResolvedValue(undefined),
  fetchParticipants: vi.fn().mockResolvedValue(undefined),
}

// Mock session store - player with character assigned
const mockStoreWithCharacter = {
  ...mockStoreNoCharacter,
  participants: [
    {
      ...mockStoreNoCharacter.participants[0],
      characterId: 'char-1',
    },
  ],
}

vi.mock('../store', () => ({
  useSessionStore: vi.fn(() => mockStoreNoCharacter),
}))

// ---------------------------------------------------------------------------
// Router setup
// ---------------------------------------------------------------------------

function createTestRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/sessions/:sessionId/play', component: PlayerSessionView },
    ],
  })
}

// ---------------------------------------------------------------------------
// Mount helper
// ---------------------------------------------------------------------------

async function mountView(participantIdOverride?: string) {
  // Set up sessionStorage
  sessionStorage.setItem('participant_id', participantIdOverride ?? 'participant-1')
  sessionStorage.setItem('participant_name', 'Thorin')

  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()
  await router.push('/sessions/session-1/play')
  await router.isReady()

  const wrapper = mount(PlayerSessionView, {
    global: {
      plugins: [pinia, router],
    },
  })
  await flushPromises()
  return wrapper
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('PlayerSessionView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  // ---------------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------------

  it('renders the session name in the header', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Friday Night Game')
  })

  it('renders the participant name in the header', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Thorin')
  })

  it('renders the Scene section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Scene')
  })

  it('renders the Character section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Character')
  })

  it('renders the Dice Tray section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Dice Tray')
  })

  it('renders the Chat section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Chat')
  })

  // ---------------------------------------------------------------------------
  // Waiting state - no character assigned
  // ---------------------------------------------------------------------------

  it('shows waiting state when no character is assigned', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Waiting for character assignment')
  })

  it('shows a helpful message in the waiting state', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('The host will assign a character to you shortly.')
  })

  // ---------------------------------------------------------------------------
  // Dice tray
  // ---------------------------------------------------------------------------

  it('renders common dice shortcut buttons', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('d20')
    expect(wrapper.text()).toContain('d6')
  })

  it('renders the dice formula input', async () => {
    const wrapper = await mountView()
    const input = wrapper.find('#dice-formula')
    expect(input.exists()).toBe(true)
  })

  it('renders the Roll button', async () => {
    const wrapper = await mountView()
    const rollButton = wrapper.findAll('button').find((b) => b.text().includes('Roll'))
    expect(rollButton).toBeTruthy()
  })

  it('renders the Manual Roll section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Manual Roll')
  })

  // ---------------------------------------------------------------------------
  // Roll requests
  // ---------------------------------------------------------------------------

  it('does not show roll request banner when there are no pending requests', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).not.toContain('Roll requested:')
  })

  // ---------------------------------------------------------------------------
  // Mobile tab navigation
  // ---------------------------------------------------------------------------

  it('renders the mobile tab navigation', async () => {
    const wrapper = await mountView()
    const nav = wrapper.find('nav[aria-label="Session sections"]')
    expect(nav.exists()).toBe(true)
  })

  it('renders all four tab buttons', async () => {
    const wrapper = await mountView()
    const nav = wrapper.find('nav[aria-label="Session sections"]')
    const buttons = nav.findAll('button')
    const labels = buttons.map((b) => b.text().toLowerCase())
    expect(labels).toContain('scene')
    expect(labels).toContain('chat')
    expect(labels).toContain('dice')
    expect(labels).toContain('character')
  })

  // ---------------------------------------------------------------------------
  // Connection indicator
  // ---------------------------------------------------------------------------

  it('renders a connection status indicator', async () => {
    const wrapper = await mountView()
    // The connection dot is a span with a rounded-full class
    const dot = wrapper.find('span.rounded-full')
    expect(dot.exists()).toBe(true)
  })
})

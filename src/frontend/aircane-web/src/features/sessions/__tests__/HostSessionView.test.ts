import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import HostSessionView from '../HostSessionView.vue'
import { SessionAccessMode, SessionStatus, ParticipantRole } from '../api'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

// Mock SignalR so we don't need a real hub
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

// Mock qrcode for LanJoinScreen
vi.mock('qrcode', () => ({
  default: {
    toCanvas: vi.fn().mockResolvedValue(undefined),
  },
}))

// Mock session API
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    endSession: vi.fn().mockResolvedValue(undefined),
  }
})

// Mock dice API
vi.mock('../../dice/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../dice/api')>()
  return {
    ...actual,
    getRollLog: vi.fn().mockResolvedValue([]),
  }
})

// Mock the session store
vi.mock('../store', () => ({
  useSessionStore: vi.fn(() => ({
    currentSession: {
      id: 'session-1',
      campaignId: 'campaign-1',
      name: 'Friday Night Game',
      accessMode: SessionAccessMode.LocalLan,
      status: SessionStatus.Active,
      startedAt: '2024-01-01T10:00:00Z',
      endedAt: null,
      createdAt: '2024-01-01T09:00:00Z',
      participantCount: 2,
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
        isApproved: false,
        joinedAt: '2024-01-01T10:00:00Z',
        lastSeenAt: '2024-01-01T10:00:00Z',
      },
    ],
    loading: false,
    error: null,
    fetchSession: vi.fn().mockResolvedValue(undefined),
    fetchParticipants: vi.fn().mockResolvedValue(undefined),
    approveParticipant: vi.fn().mockResolvedValue(undefined),
    assignCharacter: vi.fn().mockResolvedValue(undefined),
  })),
}))

// ---------------------------------------------------------------------------
// Router setup
// ---------------------------------------------------------------------------

function createTestRouter() {
  return createRouter({
    history: createWebHistory(),
    routes: [
      { path: '/sessions/:sessionId/host', component: HostSessionView },
      { path: '/sessions', component: { template: '<div>Sessions</div>' } },
    ],
  })
}

// ---------------------------------------------------------------------------
// Mount helper
// ---------------------------------------------------------------------------

async function mountView() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createTestRouter()
  await router.push('/sessions/session-1/host')
  await router.isReady()

  const wrapper = mount(HostSessionView, {
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

describe('HostSessionView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the session name in the header', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Friday Night Game')
  })

  it('renders the End Session button', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('End Session')
  })

  it('renders the Participants section via ParticipantApprovalPanel', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Participants')
  })

  it('renders the pending participant in the approval panel', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Thorin')
  })

  it('renders the AI proposals placeholder', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Pending AI Proposals')
  })

  it('renders the Chat section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Chat')
  })

  it('renders the Roll Log section', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Roll Log')
  })

  it('shows "No rolls yet." when the roll log is empty', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('No rolls yet.')
  })

  it('does not show the LAN join screen when inviteCode is null', async () => {
    const wrapper = await mountView()
    // LanJoinScreen is only shown when inviteCode is present
    expect(wrapper.find('canvas').exists()).toBe(false)
  })

  it('shows Active status label', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Active')
  })
})

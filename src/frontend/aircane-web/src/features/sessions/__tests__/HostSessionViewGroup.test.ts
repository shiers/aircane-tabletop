import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import HostSessionView from '../HostSessionView.vue'
import {
  ParticipantRole,
  SessionAccessMode,
  SessionStatus,
  type NetworkInfo,
  type ParticipantDto,
  type SessionDto,
} from '../api'
import { saveInviteCode, loadInviteCode } from '../inviteCodeStorage'
import * as sessionsApi from '../api'
import * as diceApi from '../../dice/api'
import * as adventuresApi from '@/features/adventures/api'
import { groupRunState, makeState } from '@/features/adventures/__tests__/fixtures'

// ── Mocks ────────────────────────────────────────────────────────────────────

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  connection: {
    on: vi.fn(),
    onreconnected: vi.fn(),
    onreconnecting: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn(),
    invoke: vi.fn(),
    stop: vi.fn(),
  },
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn().mockReturnValue({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
    build: vi.fn(() => hub.connection),
  }),
  LogLevel: { Warning: 1 },
}))

vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  getSession: vi.fn(),
  getParticipants: vi.fn(),
  getNetworkInfo: vi.fn(),
  approveParticipant: vi.fn(),
  denyParticipant: vi.fn(),
  removeParticipant: vi.fn(),
  assignCharacter: vi.fn(),
  endSession: vi.fn(),
  getChatHistory: vi.fn(),
}))

vi.mock('@/features/adventures/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventures/api')>()),
  getActiveAdventureSession: vi.fn(),
  getAdventureSession: vi.fn(),
  startGroupAdventureSession: vi.fn(),
  addAdventureParticipant: vi.fn(),
}))

vi.mock('@/features/characters/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/characters/api')>()),
  listCharacters: vi.fn(),
}))

vi.mock('@/features/adventure-generation/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventure-generation/api')>()),
  getAdventureDraft: vi.fn(),
}))

vi.mock('../../dice/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../dice/api')>()),
  getRollLog: vi.fn().mockResolvedValue([]),
}))

vi.mock('../combat', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../combat')>()),
  getEncounter: vi.fn().mockResolvedValue(null),
}))

import * as charactersApi from '@/features/characters/api'
import * as generationApi from '@/features/adventure-generation/api'
import { features } from '@/shared/features'

// ── Fixtures ─────────────────────────────────────────────────────────────────

const SESSION: SessionDto = {
  id: 'play-1',
  campaignId: 'campaign-1',
  name: 'Group: The Sunken Archive',
  accessMode: SessionAccessMode.LocalLan,
  status: SessionStatus.Active,
  startedAt: '2026-10-01T10:00:00Z',
  endedAt: null,
  createdAt: '2026-10-01T09:00:00Z',
  participantCount: 0,
  inviteCode: null, // the server returns it once, at creation; a reload never sees it
  joinUrl: null,
}

const NETWORK: NetworkInfo = {
  localUrl: 'http://localhost:5000',
  lanUrl: 'http://192.168.1.42:5000',
  inviteCode: null,
  tunnelUrl: null,
  tunnelActive: false,
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

async function mountHost(options: { adventure?: boolean; participants?: ParticipantDto[] } = {}) {
  vi.mocked(sessionsApi.getSession).mockResolvedValue(SESSION)
  vi.mocked(sessionsApi.getParticipants).mockResolvedValue(options.participants ?? [])
  vi.mocked(sessionsApi.getNetworkInfo).mockResolvedValue(NETWORK)

  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sessions/:sessionId/host', name: 'host-session', component: HostSessionView },
      { path: '/sessions', component: { template: '<div>Sessions</div>' } },
    ],
  })
  await router.push(options.adventure === false ? '/sessions/play-1/host' : '/sessions/play-1/host?adventure=adventure-1')
  await router.isReady()

  const wrapper = mount(HostSessionView, { global: { plugins: [pinia, router] } })
  await flushPromises()
  return { wrapper, router }
}

const text = (wrapper: ReturnType<typeof mount>) => wrapper.text().replace(/\s+/g, ' ')

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  hub.handlers.clear()
  hub.connection.on.mockImplementation((name: string, cb: (...a: unknown[]) => void) => hub.handlers.set(name, cb))
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
  vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue(null)
  vi.mocked(charactersApi.listCharacters).mockResolvedValue([])
  vi.mocked(sessionsApi.getChatHistory).mockResolvedValue({ messages: [], hasMore: false })
  vi.mocked(generationApi.getAdventureDraft).mockResolvedValue({
    id: 'adventure-1', title: 'The Sunken Archive', status: 'Approved', createdAt: '', updatedAt: '',
  })
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

// ── Tests ────────────────────────────────────────────────────────────────────

describe('HostSessionView: group lobby', () => {
  it('shows the lobby when the host arrives from "Start Group Session"', async () => {
    const { wrapper } = await mountHost()

    expect(wrapper.find('[data-testid="group-lobby"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(false)
    expect(text(wrapper)).toContain('The Sunken Archive')
  })

  it('does not show the lobby for an ordinary session', async () => {
    const { wrapper } = await mountHost({ adventure: false })

    expect(wrapper.find('[data-testid="group-lobby"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(false)
  })

  it('shows the invite code that was kept when the session was created, after a reload', async () => {
    saveInviteCode('play-1', 'ABCD1234')

    const { wrapper } = await mountHost()

    expect(text(wrapper)).toContain('ABCD1234')
    expect(wrapper.find('[data-testid="no-invite-code"]').exists()).toBe(false)
  })

  it('explains when the invite code is gone', async () => {
    const { wrapper } = await mountHost()

    expect(wrapper.find('[data-testid="no-invite-code"]').exists()).toBe(true)
  })

  it('builds the join link from the LAN address, not localhost', async () => {
    saveInviteCode('play-1', 'ABCD1234')

    const { wrapper } = await mountHost()

    // jsdom serves the page from http://localhost:3000.
    expect(text(wrapper)).toContain('http://192.168.1.42:3000/join/play-1')
    expect(text(wrapper)).not.toContain('localhost:3000/join')
  })

  it('lists pending players for approval and approves them through the server', async () => {
    vi.mocked(sessionsApi.approveParticipant).mockResolvedValue(person('newcomer'))
    const { wrapper } = await mountHost({ participants: [person('newcomer', { isApproved: false })] })

    await wrapper.find('[aria-label="Approve newcomer"]').trigger('click')
    await flushPromises()

    expect(sessionsApi.approveParticipant).toHaveBeenCalledWith('play-1', 'newcomer')
  })

  it('offers the campaigns player characters for assignment', async () => {
    vi.mocked(charactersApi.listCharacters).mockResolvedValue([
      { id: 'c1', name: 'Mira', role: charactersApi.CharacterRole.Player },
      { id: 'c2', name: 'Goblin King', role: charactersApi.CharacterRole.Npc },
    ] as never)

    const { wrapper } = await mountHost({ participants: [person('alice')] })

    const options = wrapper.find('select[aria-label="Assign character to alice"]').findAll('option').map((o) => o.text())
    expect(options).toEqual(['Select character…', 'Mira'])
  })

  it('disables Start until a player is approved, then starts with the approved players in order', async () => {
    const run = groupRunState({ sessionId: 'play-1' })
    vi.mocked(adventuresApi.startGroupAdventureSession).mockResolvedValue(run)
    const { wrapper } = await mountHost({
      participants: [person('alice'), person('bob'), person('waiting', { isApproved: false })],
    })

    await wrapper.find('[data-testid="start-adventure-button"]').trigger('click')
    await flushPromises()

    // This lobby's play session goes with the request, with the turn-advance choice (manual by default).
    expect(adventuresApi.startGroupAdventureSession).toHaveBeenCalledWith({
      generatedAdventureId: 'adventure-1',
      campaignId: 'campaign-1',
      participantIds: ['alice', 'bob'],
      playSessionId: 'play-1',
      turnAdvance: 'ManualOnly',
    })
    // The run view replaces the lobby.
    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-lobby"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Alice')
  })

  it('disables Start when nobody is approved', async () => {
    const { wrapper } = await mountHost({ participants: [person('waiting', { isApproved: false })] })

    expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
  })

  it('blocks Start, with the reason, while the campaign has a solo adventure in progress', async () => {
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'solo-run' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(makeState({ id: 'solo-run', adventureTitle: 'Old Tale' }))

    const { wrapper } = await mountHost({ participants: [person('alice')] })

    expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="start-reason"]').text()).toContain('Old Tale')
  })

  it('refreshes the player list from the hub when someone joins or is approved, without polling', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { wrapper } = await mountHost()
    expect(wrapper.find('[data-testid="turn-order-empty"]').exists()).toBe(true)
    vi.mocked(sessionsApi.getParticipants).mockClear()
    vi.mocked(sessionsApi.getParticipants).mockResolvedValue([person('alice')])

    // No timer reads the roster while the hub is up...
    await vi.advanceTimersByTimeAsync(30_000)
    expect(sessionsApi.getParticipants).not.toHaveBeenCalled()

    // ...the hub's event does.
    hub.handlers.get('ParticipantApproved')!({ sessionId: 'play-1', participantId: 'alice', displayName: 'alice', autoApproved: true })
    await flushPromises()

    expect(wrapper.findAll('[data-testid="turn-order-item"]')).toHaveLength(1)
    expect(wrapper.find('[data-testid="start-adventure-button"]').attributes('disabled')).toBeUndefined()
  })

  it('stops polling when the host leaves the screen', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { wrapper } = await mountHost()
    wrapper.unmount()
    vi.mocked(sessionsApi.getParticipants).mockClear()

    await vi.advanceTimersByTimeAsync(12000)

    expect(sessionsApi.getParticipants).not.toHaveBeenCalled()
  })

  it('does not poll an ordinary session', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    await mountHost({ adventure: false })
    vi.mocked(sessionsApi.getParticipants).mockClear()

    await vi.advanceTimersByTimeAsync(12000)

    expect(sessionsApi.getParticipants).not.toHaveBeenCalled()
  })
})

describe('HostSessionView: group run', () => {
  async function mountRunning() {
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(groupRunState({ sessionId: 'play-1' }))
    return mountHost({ adventure: false, participants: [person('p-alice'), person('p-bob'), person('p-cara')] })
  }

  it('shows the run view on a plain reload, because the server says a group run exists', async () => {
    const { wrapper } = await mountRunning()

    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Alice')
    expect(wrapper.find('[data-testid="group-lobby"]').exists()).toBe(false)
  })

  it('shows the run, not the lobby, even when the lobby link is opened again', async () => {
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(groupRunState({ sessionId: 'play-1' }))

    const { wrapper } = await mountHost({ participants: [person('p-alice')] })

    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="group-lobby"]').exists()).toBe(false)
  })

  it('does not show the group run for a solo run', async () => {
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'solo' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(makeState({ id: 'solo' }))

    const { wrapper } = await mountHost({ adventure: false })

    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(false)
  })

  it('refreshes when the hub reports the turn was passed', async () => {
    const { wrapper } = await mountRunning()
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(
      groupRunState({ sessionId: 'play-1', activeParticipantName: 'Bob' }),
    )

    hub.handlers.get('TurnAdvanced')!({
      sessionId: 'run-1',
      previousParticipantId: 'p-alice',
      activeParticipantId: 'p-bob',
      activeParticipantName: 'Bob',
      currentRound: 1,
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Bob')
  })

  it('handles ParticipantJoinedAdventure: shows a note and the new turn order', async () => {
    const { wrapper } = await mountRunning()
    const withDan = groupRunState({ sessionId: 'play-1' })
    withDan.turnOrder = [...withDan.turnOrder, { participantId: 'p-dan', participantName: 'Dan', isActive: false }]
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(withDan)

    hub.handlers.get('ParticipantJoinedAdventure')!({
      sessionId: 'run-1',
      participantId: 'p-dan',
      participantName: 'Dan',
      turnPosition: 3,
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="run-notice"]').text()).toBe('Dan joined the adventure as player 4.')
    expect(wrapper.findAll('[data-testid="run-turn-order"] li')).toHaveLength(4)
  })

  it.each(['AdventureSceneAdvanced', 'AdventureClueRevealed', 'AdventureSecretRevealed', 'AdventureNpcUpdated', 'AdventureCompleted'])(
    'refreshes on %s',
    async (event) => {
      await mountRunning()
      vi.mocked(adventuresApi.getAdventureSession).mockClear()

      hub.handlers.get(event)!({ sessionId: 'run-1' })
      await flushPromises()

      expect(adventuresApi.getAdventureSession).toHaveBeenCalledWith('run-1')
    },
  )

  it('offers approved players who are not in the turn order, so a late joiner can be added', async () => {
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(groupRunState({ sessionId: 'play-1' }))

    const { wrapper } = await mountHost({
      adventure: false,
      participants: [person('p-alice'), person('p-bob'), person('p-cara'), person('p-dan')],
    })

    expect(wrapper.find('[data-testid="late-players"]').text()).toContain('p-dan')
  })
})

describe('HostSessionView: inviting players after the adventure has started', () => {
  async function mountRunning(participants: ParticipantDto[], saveCode = true) {
    if (saveCode) saveInviteCode('play-1', 'ABCD1234')
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(groupRunState({ sessionId: 'play-1' }))
    return mountHost({ adventure: false, participants })
  }

  const inRun = [person('p-alice'), person('p-bob'), person('p-cara')]

  // The bug: starting the adventure replaced the lobby, and the join link, QR and code vanished.
  it('keeps the join link, QR code and invite code in the run view', async () => {
    const { wrapper } = await mountRunning(inRun)

    expect(wrapper.find('[data-testid="host-run-view"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="invite-panel"]').exists()).toBe(true)
    // jsdom serves the page from http://localhost:3000; the link uses the LAN address instead.
    expect(wrapper.find('[data-testid="invite-url"]').text()).toBe('http://192.168.1.42:3000/join/play-1')
    expect(wrapper.find('[data-testid="invite-code"]').text()).toBe('ABCD1234')
    expect(wrapper.find('[data-testid="invite-panel"] canvas').exists()).toBe(true)
  })

  it('is open on a wide screen (jsdom reports no narrow screen)', async () => {
    const { wrapper } = await mountRunning(inRun)

    expect(wrapper.find('[data-testid="invite-toggle"]').attributes('aria-expanded')).toBe('true')
  })

  it('shows the missing-code message, and the join link, when this browser no longer has the code', async () => {
    const { wrapper } = await mountRunning(inRun, false)

    expect(wrapper.find('[data-testid="invite-code-missing"]').text()).toMatch(/can't be shown again/i)
    expect(wrapper.find('[data-testid="new-session-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="invite-url"]').exists()).toBe(true)
  })

  it('puts a badge on the panel when someone is waiting, counting requests and players to add', async () => {
    const { wrapper } = await mountRunning([
      ...inRun,
      person('waiting', { isApproved: false }),
      person('p-dan'), // approved, not in the turn order
    ])

    expect(wrapper.find('[data-testid="invite-badge"]').text()).toContain('2')
  })

  it('has no badge when everyone is in', async () => {
    const { wrapper } = await mountRunning(inRun)

    expect(wrapper.find('[data-testid="invite-badge"]').exists()).toBe(false)
  })

  it('approves a late joiner from the panel', async () => {
    vi.mocked(sessionsApi.approveParticipant).mockResolvedValue(person('p-dan'))
    const { wrapper } = await mountRunning([...inRun, person('p-dan', { isApproved: false })])

    await wrapper.find('[data-testid="invite-approve"]').trigger('click')
    await flushPromises()

    expect(sessionsApi.approveParticipant).toHaveBeenCalledWith('play-1', 'p-dan')
  })

  it('adds an approved late player to the end of the turn order, from the panel', async () => {
    const withDan = groupRunState({ sessionId: 'play-1' })
    withDan.turnOrder = [...withDan.turnOrder, { participantId: 'p-dan', participantName: 'Dan', isActive: false }]
    vi.mocked(adventuresApi.addAdventureParticipant).mockResolvedValue(withDan)
    const { wrapper } = await mountRunning([...inRun, person('p-dan')])

    await wrapper.find('[data-testid="add-late-player"]').trigger('click')
    await flushPromises()

    expect(adventuresApi.addAdventureParticipant).toHaveBeenCalledWith('run-1', 'p-dan')
    expect(wrapper.findAll('[data-testid="run-turn-order"] li')).toHaveLength(4)
    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Alice') // the turn stays put
    expect(wrapper.find('[data-testid="run-progress"]').text()).toContain('Round 1') // and so does the round
  })

  it('moves a player from "waiting" to "to add" once they are approved (the list refreshes)', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { wrapper } = await mountRunning([...inRun, person('p-dan', { isApproved: false })])
    expect(wrapper.find('[data-testid="invite-pending"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="add-late-player"]').exists()).toBe(false)
    vi.mocked(sessionsApi.getParticipants).mockResolvedValue([...inRun, person('p-dan')])

    hub.handlers.get('ParticipantApproved')!({ sessionId: 'play-1', participantId: 'p-dan', displayName: 'Dan', autoApproved: false })
    await flushPromises()

    expect(wrapper.find('[data-testid="invite-pending"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="add-late-player"]').exists()).toBe(true)
  })

  it('does not offer the panel outside a group run', async () => {
    const { wrapper } = await mountHost({ adventure: false })

    expect(wrapper.find('[data-testid="invite-panel"]').exists()).toBe(false)
  })
})

describe('HostSessionView: live updates (SignalR)', () => {
  it('joins the play sessions group with the message object the hub expects', async () => {
    await mountHost()

    expect(hub.connection.invoke).toHaveBeenCalledWith(
      'JoinSession',
      expect.objectContaining({
        sessionId: 'play-1',
        displayName: 'Host',
        role: 0, // ParticipantRole.Host
        participantId: expect.any(String),
      }),
    )
  })

  it('subscribes to the group run events', async () => {
    await mountHost()

    for (const event of [
      'TurnAdvanced',
      'ParticipantJoinedAdventure',
      'AdventureSceneAdvanced',
      'AdventureClueRevealed',
      'AdventureSecretRevealed',
      'AdventureNpcUpdated',
      'AdventureCompleted',
    ]) {
      expect(hub.handlers.has(event), event).toBe(true)
    }
  })

  it('joins the group again after a reconnect', async () => {
    await mountHost()
    const reconnected = hub.connection.onreconnected.mock.calls[0][0] as () => Promise<void>
    hub.connection.invoke.mockClear()

    await reconnected()

    expect(hub.connection.invoke).toHaveBeenCalledWith('JoinSession', expect.objectContaining({ sessionId: 'play-1' }))
  })

  it('shows a chat message from a player (the hub sends one notification object)', async () => {
    const { wrapper } = await mountHost({ adventure: false })

    hub.handlers.get('ChatMessageReceived')!({
      sessionId: 'play-1',
      participantId: 'p-alice',
      displayName: 'Alice',
      text: 'Ready when you are',
      sentAt: '2026-10-01T10:00:00Z',
    })
    await flushPromises()

    expect(text(wrapper)).toContain('Alice')
    expect(text(wrapper)).toContain('Ready when you are')
  })
})

describe('HostSessionView: ending the session', () => {
  it('forgets the saved invite code', async () => {
    vi.stubGlobal('confirm', vi.fn(() => true))
    vi.mocked(sessionsApi.endSession).mockResolvedValue(undefined)
    saveInviteCode('play-1', 'ABCD1234')
    const { wrapper } = await mountHost()
    expect(loadInviteCode('play-1')).toBe('ABCD1234')

    const end = wrapper.findAll('button').find((b) => b.text() === 'End Session')!
    await end.trigger('click')
    await flushPromises()

    expect(sessionsApi.endSession).toHaveBeenCalledWith('play-1')
    expect(loadInviteCode('play-1')).toBeNull()
  })

  it('keeps the invite code if the host cancels', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    saveInviteCode('play-1', 'ABCD1234')
    const { wrapper } = await mountHost()

    await wrapper.findAll('button').find((b) => b.text() === 'End Session')!.trigger('click')
    await flushPromises()

    expect(sessionsApi.endSession).not.toHaveBeenCalled()
    expect(loadInviteCode('play-1')).toBe('ABCD1234')
  })
})

describe('HostSessionView: chat history, roll log and character assignment', () => {
  const said = (id: string, minute: number, overrides: Record<string, unknown> = {}) => ({
    id,
    sessionId: 'play-1',
    senderId: 'player-1',
    senderName: 'Alice',
    senderRole: 2,
    text: `line ${id}`,
    sentAt: `2026-10-20T10:${String(minute).padStart(2, '0')}:00Z`,
    ...overrides,
  })

  const rollBy = (id: string, overrides: Record<string, unknown> = {}) => ({
    id,
    sessionId: 'play-1',
    characterId: null,
    rollerParticipantId: 'player-1',
    rollerName: 'Alice',
    formula: '1d20+2',
    dieResults: [15],
    modifier: 2,
    total: 17,
    isManual: false,
    visibility: 0,
    context: 'Perception',
    createdAt: '2026-10-20T10:08:00Z',
    ...overrides,
  })

  beforeEach(() => {
    vi.mocked(diceApi.getRollLog).mockResolvedValue([])
  })

  it('loads the stored chat when the host screen opens', async () => {
    vi.mocked(sessionsApi.getChatHistory).mockResolvedValue({ messages: [said('a', 1), said('b', 2)], hasMore: false })

    const { wrapper } = await mountHost()

    expect(sessionsApi.getChatHistory).toHaveBeenCalledWith('play-1')
    expect(text(wrapper)).toContain('line a')
    expect(text(wrapper)).toContain('line b')
  })

  it('shows a message once when it is in the history and arrives live too', async () => {
    vi.mocked(sessionsApi.getChatHistory).mockResolvedValue({ messages: [said('a', 1)], hasMore: false })
    const { wrapper } = await mountHost()

    hub.handlers.get('ChatMessageReceived')!({
      sessionId: 'play-1', participantId: 'player-1', displayName: 'Alice', text: 'line a',
      sentAt: '2026-10-20T10:01:00Z', messageId: 'a', role: 'Player',
    })
    await flushPromises()

    expect(text(wrapper).match(/line a/g)).toHaveLength(1)
  })

  it('pages up through the chat history', async () => {
    vi.mocked(sessionsApi.getChatHistory)
      .mockResolvedValueOnce({ messages: [said('c', 3)], hasMore: true })
      .mockResolvedValueOnce({ messages: [said('b', 2)], hasMore: false })
    const { wrapper } = await mountHost()

    await wrapper.find('[data-testid="load-older"]').trigger('click')
    await flushPromises()

    expect(sessionsApi.getChatHistory).toHaveBeenLastCalledWith('play-1', { before: 'c' })
    expect(text(wrapper)).toContain('line b')
    expect(wrapper.find('[data-testid="load-older"]').exists()).toBe(false)
  })

  it('reads the chat and the roll log again after a reconnect, which may have missed both', async () => {
    await mountHost()
    vi.mocked(sessionsApi.getChatHistory).mockClear()
    vi.mocked(diceApi.getRollLog).mockClear()
    const onReconnected = hub.connection.onreconnected.mock.calls[0][0] as () => Promise<void>

    await onReconnected()
    await flushPromises()

    expect(sessionsApi.getChatHistory).toHaveBeenCalledTimes(1)
    expect(diceApi.getRollLog).toHaveBeenCalledTimes(1)
  })

  it('shows the stored roll log, with who rolled, the formula, the dice and the total', async () => {
    vi.mocked(diceApi.getRollLog).mockResolvedValue([rollBy('r1')] as never)

    const { wrapper } = await mountHost()

    expect(wrapper.find('[aria-label="Roll log"]').text()).toContain('Alice: 1d20+2 [15] +2 = 17 (Perception)')
  })

  it('shows a public roll made on another device (a player\'s phone), live', async () => {
    const { wrapper } = await mountHost()

    hub.handlers.get('RollRecorded')!({ sessionId: 'play-1', roll: rollBy('live-1', { context: 'Athletics' }) })
    await flushPromises()

    expect(wrapper.find('[aria-label="Roll log"]').text()).toContain('Alice: 1d20+2 [15] +2 = 17 (Athletics)')
  })

  it('shows a player\'s private roll to the host, marked private', async () => {
    const { wrapper } = await mountHost()

    hub.handlers.get('RollRecorded')!({ sessionId: 'play-1', roll: rollBy('secret', { visibility: 1, context: 'secret insight' }) })
    await flushPromises()

    const entry = wrapper.find('[data-testid="roll-entry"]')
    expect(entry.attributes('data-visibility')).toBe('private')
    expect(entry.text()).toContain('secret insight')
  })

  it('shows a roll once when it is in the history and arrives live', async () => {
    vi.mocked(diceApi.getRollLog).mockResolvedValue([rollBy('r1')] as never)
    const { wrapper } = await mountHost()

    hub.handlers.get('RollRecorded')!({ sessionId: 'play-1', roll: rollBy('r1') })
    await flushPromises()

    expect(wrapper.findAll('[data-testid="roll-entry"]')).toHaveLength(1)
  })

  it('offers the campaign\'s characters for assignment in an ordinary session too, not only while setting up an adventure', async () => {
    vi.mocked(charactersApi.listCharacters).mockResolvedValue([
      { id: 'c1', name: 'Mira', role: charactersApi.CharacterRole.Player },
    ] as never)

    const { wrapper } = await mountHost({ adventure: false, participants: [person('alice')] })

    const select = wrapper.find('select[aria-label="Assign character to alice"]')
    expect(select.exists()).toBe(true)
    expect(select.findAll('option').map((o) => o.text())).toEqual(['Select character…', 'Mira'])
  })

  it('says "AI GM runtime" in the proposals placeholder', async () => {
    const { wrapper } = await mountHost({ adventure: false })

    expect(text(wrapper)).toContain('AI proposals will appear here when the AI GM runtime is active.')
  })
})

describe('HostSessionView: deny, remove and presence', () => {
  async function mountRunning(participants: ParticipantDto[], run = groupRunState({ sessionId: 'play-1' })) {
    saveInviteCode('play-1', 'ABCD1234')
    vi.mocked(adventuresApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(adventuresApi.getAdventureSession).mockResolvedValue(run)
    return mountHost({ adventure: false, participants })
  }

  const inRun = [
    person('p-alice', { displayName: 'Alice', isConnected: true }),
    person('p-bob', { displayName: 'Bob', isConnected: true }),
    person('p-cara', { displayName: 'Cara', isConnected: true }),
  ]

  it('Deny asks, naming the person, then declines the request on the server', async () => {
    vi.mocked(sessionsApi.denyParticipant).mockResolvedValue(person('walt', { isApproved: false, isDenied: true }))
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountHost({ adventure: false, participants: [person('walt', { displayName: 'Walt', isApproved: false })] })

    await wrapper.find('[data-testid="deny-participant"]').trigger('click')
    await flushPromises()
    expect(confirmFn.mock.calls[0][0]).toContain("Decline Walt's request to join?")
    expect(sessionsApi.denyParticipant).not.toHaveBeenCalled()

    confirmFn.mockReturnValue(true)
    await wrapper.find('[data-testid="deny-participant"]').trigger('click')
    await flushPromises()
    expect(sessionsApi.denyParticipant).toHaveBeenCalledWith('play-1', 'walt')
    expect(wrapper.find('[data-testid="deny-participant"]').exists()).toBe(false)
  })

  it("shows the server's message when a denial fails", async () => {
    vi.mocked(sessionsApi.denyParticipant).mockRejectedValue(
      Object.assign(new Error('Request failed with status code 409'), {
        isAxiosError: true,
        response: { status: 409, data: { detail: 'That player is already approved and taking part.' } },
      }),
    )
    vi.stubGlobal('confirm', vi.fn(() => true))
    const { wrapper } = await mountHost({ adventure: false, participants: [person('walt', { isApproved: false })] })

    await wrapper.find('[data-testid="deny-participant"]').trigger('click')
    await flushPromises()

    expect(text(wrapper)).toContain('That player is already approved and taking part.')
  })

  it('Remove names the person and what happens: the seat, the turn moving on, the character', async () => {
    vi.mocked(sessionsApi.removeParticipant).mockResolvedValue({
      participant: person('p-alice', { isApproved: false, isRemoved: true }),
      adventureRunIds: ['run-1'],
      turnPassedInRunIds: ['run-1'],
    })
    vi.mocked(charactersApi.listCharacters).mockResolvedValue([
      { id: 'char-mira', name: 'Mira', role: 0 } as never,
    ])
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const participants = [{ ...inRun[0], characterId: 'char-mira' }, inRun[1], inRun[2]]
    const { wrapper } = await mountRunning(participants)

    // Alice is acting: remove her from the participants panel.
    await wrapper.findAll('[data-testid="remove-participant"]')[0].trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls[0][0] as string
    expect(asked).toContain('Remove Alice from this session?')
    expect(asked).toContain('loses their seat')
    expect(asked).toContain('the turn moves on to Bob')
    expect(asked).toContain('Their character, Mira, is unassigned.')
    expect(sessionsApi.removeParticipant).toHaveBeenCalledWith('play-1', 'p-alice')
    expect(adventuresApi.getAdventureSession).toHaveBeenCalledTimes(2)   // the run is read again
  })

  it('a declined removal removes nothing', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    const { wrapper } = await mountRunning(inRun)

    await wrapper.findAll('[data-testid="remove-turn-player"]')[1].trigger('click')
    await flushPromises()

    expect(sessionsApi.removeParticipant).not.toHaveBeenCalled()
  })

  it('removing someone who is not acting says they leave the turn order', async () => {
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountRunning(inRun)

    await wrapper.findAll('[data-testid="remove-turn-player"]')[2].trigger('click')

    const asked = confirmFn.mock.calls[0][0] as string
    expect(asked).toContain('Remove Cara from this session?')
    expect(asked).toContain("leave the adventure's turn order")
    expect(asked).not.toContain('turn moves on')
  })

  it('shows who is connected, and follows the hub as people come and go', async () => {
    const { wrapper } = await mountRunning(inRun)
    const dots = () => wrapper.findAll('[data-testid="turn-presence-dot"]').map((d) => d.attributes('aria-label'))
    expect(dots()).toEqual(['Connected', 'Connected', 'Connected'])

    hub.handlers.get('ParticipantPresenceChanged')!({ sessionId: 'play-1', participantId: 'p-bob', isConnected: false })
    await flushPromises()

    expect(dots()).toEqual(['Connected', 'Away', 'Connected'])
  })

  it('says when the acting player is disconnected, beside Pass the turn, and offers Remove there', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    const { wrapper } = await mountRunning(inRun)
    expect(wrapper.find('[data-testid="acting-player-away"]').exists()).toBe(false)

    hub.handlers.get('ParticipantPresenceChanged')!({ sessionId: 'play-1', participantId: 'p-alice', isConnected: false })
    await flushPromises()

    const away = wrapper.find('[data-testid="acting-player-away"]')
    expect(away.text()).toContain('Alice is disconnected')
    // Nothing changed by itself: Alice still holds the turn.
    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Alice')
    await away.find('[data-testid="remove-acting-player"]').trigger('click')
    expect(vi.mocked(confirm)).toHaveBeenCalledWith(expect.stringContaining('Remove Alice from this session?'))
  })

  it('with nobody in the turn order, says there is no acting player', async () => {
    const empty = groupRunState({ sessionId: 'play-1' })
    empty.turnOrder = []
    empty.activeParticipantId = null
    empty.activeParticipantName = null
    const { wrapper } = await mountRunning([], empty)

    expect(wrapper.find('[data-testid="no-acting-player"]').text()).toBe('No acting player. Approve and add a player to continue.')
    expect(wrapper.find('[data-testid="advance-turn-button"]').attributes('disabled')).toBeDefined()
  })

  it('re-reads the roster on each roster event, and the run when it changes', async () => {
    await mountRunning(inRun)
    vi.mocked(sessionsApi.getParticipants).mockClear()
    vi.mocked(adventuresApi.getAdventureSession).mockClear()

    for (const name of ['ParticipantJoined', 'ParticipantApproved', 'ParticipantDenied', 'ParticipantRemoved', 'ParticipantLeft']) {
      hub.handlers.get(name)!({ sessionId: 'play-1', participantId: 'x', displayName: 'X' })
    }
    hub.handlers.get('AdventureStateChanged')!({ sessionId: 'run-1', reason: 'ParticipantRemoved', changedAt: '' })
    await flushPromises()

    expect(sessionsApi.getParticipants).toHaveBeenCalledTimes(5)
    expect(adventuresApi.getAdventureSession).toHaveBeenCalledWith('run-1')
  })

  it('polls slowly only while the hub connection is down', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    await mountRunning(inRun)
    vi.mocked(sessionsApi.getParticipants).mockClear()

    await vi.advanceTimersByTimeAsync(60_000)
    expect(sessionsApi.getParticipants).not.toHaveBeenCalled()

    // The connection drops: a slow poll stands in.
    const reconnecting = hub.connection.onreconnecting.mock.calls[0][0] as () => void
    reconnecting()
    await vi.advanceTimersByTimeAsync(15_000)
    expect(sessionsApi.getParticipants).toHaveBeenCalledTimes(1)

    // It comes back: the poll stops (after one catch-up read).
    const reconnected = hub.connection.onreconnected.mock.calls[0][0] as () => Promise<void>
    await reconnected()
    vi.mocked(sessionsApi.getParticipants).mockClear()
    await vi.advanceTimersByTimeAsync(60_000)
    expect(sessionsApi.getParticipants).not.toHaveBeenCalled()
  })
})

describe('HostSessionView and the Characters feature flag', () => {
  afterEach(() => {
    features.characters = true
  })

  const mira = [{ id: 'c1', name: 'Mira', role: charactersApi.CharacterRole.Player }] as never

  it('with the feature on, reads the campaigns characters and offers them for assignment', async () => {
    features.characters = true
    vi.mocked(charactersApi.listCharacters).mockResolvedValue(mira)

    const { wrapper } = await mountHost({ participants: [person('alice')] })

    expect(charactersApi.listCharacters).toHaveBeenCalledWith('campaign-1')
    expect(wrapper.find('select[aria-label="Assign character to alice"]').exists()).toBe(true)
  })

  it('with the feature off, does not read the characters, and offers no assign dropdown or Character label', async () => {
    features.characters = false
    vi.mocked(charactersApi.listCharacters).mockResolvedValue(mira)

    const { wrapper } = await mountHost({ participants: [person('alice')] })

    expect(charactersApi.listCharacters).not.toHaveBeenCalled()
    expect(wrapper.find('select[aria-label="Assign character to alice"]').exists()).toBe(false)
    expect(text(wrapper)).not.toContain('Select character')
    expect(text(wrapper)).not.toContain('Character:')
  })

  it.each([
    [true, true],
    [false, false],
  ])('with the feature %s, the Remove confirmation mentions the character: %s', async (on, mentions) => {
    features.characters = on
    const confirmFn = vi.fn((_message: string) => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountHost({
      adventure: false,
      participants: [person('p-alice', { displayName: 'Alice', characterId: 'char-mira' })],
    })

    await wrapper.find('[data-testid="remove-participant"]').trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls[0][0]
    expect(asked).toContain('Remove Alice from this session?')
    expect(/character/i.test(asked)).toBe(mentions)
  })
})

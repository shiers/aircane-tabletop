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
import * as adventuresApi from '@/features/adventures/api'
import { groupRunState, makeState } from '@/features/adventures/__tests__/fixtures'

// ── Mocks ────────────────────────────────────────────────────────────────────

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  connection: {
    on: vi.fn(),
    onreconnected: vi.fn(),
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
  assignCharacter: vi.fn(),
  endSession: vi.fn(),
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

    expect(adventuresApi.startGroupAdventureSession).toHaveBeenCalledWith({
      generatedAdventureId: 'adventure-1',
      campaignId: 'campaign-1',
      participantIds: ['alice', 'bob'],
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

  it('refreshes the player list on its own, for players who join or are approved by REST', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { wrapper } = await mountHost()
    expect(wrapper.find('[data-testid="turn-order-empty"]').exists()).toBe(true)
    vi.mocked(sessionsApi.getParticipants).mockResolvedValue([person('alice')])

    await vi.advanceTimersByTimeAsync(4000)
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

    await vi.advanceTimersByTimeAsync(4000)
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

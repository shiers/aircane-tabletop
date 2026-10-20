import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { AxiosError, type AxiosResponse } from 'axios'
import PlayerSessionView from '../PlayerSessionView.vue'
import * as sessionsApi from '../api'
import * as diceApi from '../../dice/api'
import * as charactersApi from '../../characters/api'
import type { PlayerAdventureRun } from '../api'
import { SessionAccessMode, SessionStatus, ParticipantRole } from '../api'
import { saveParticipantSession, loadParticipantSession } from '../participantSession'
import { getOrCreateRejoinSecret, getRejoinSecret } from '../rejoinSecrets'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

/** What the fake SignalR connection records, so a test can play the server. */
const hub = vi.hoisted(() => ({
  handlers: {} as Record<string, (...args: unknown[]) => void>,
  lifecycle: {} as { reconnecting?: () => void; reconnected?: () => void; close?: () => void },
  invoke: undefined as unknown as Mock,
  start: undefined as unknown as Mock,
  builds: 0,
}))

vi.mock('@microsoft/signalr', () => {
  return {
    HubConnectionBuilder: class {
      withUrl() { return this }
      withAutomaticReconnect() { return this }
      configureLogging() { return this }
      build() {
        hub.builds++
        return {
          on: (name: string, handler: (...args: unknown[]) => void) => { hub.handlers[name] = handler },
          onreconnecting: (h: () => void) => { hub.lifecycle.reconnecting = h },
          onreconnected: (h: () => void) => { hub.lifecycle.reconnected = h },
          onclose: (h: () => void) => { hub.lifecycle.close = h },
          start: (...a: unknown[]) => hub.start(...a),
          invoke: (...a: unknown[]) => hub.invoke(...a),
          stop: () => Promise.resolve(),
        }
      }
    },
    LogLevel: { Warning: 1 },
  }
})

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
    recordManualRoll: vi.fn(),
    getRollLog: vi.fn().mockResolvedValue([]),
  }
})

vi.mock('../../characters/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../characters/api')>()
  return { ...actual, listCharacters: vi.fn().mockResolvedValue([]), listAllCharacters: vi.fn().mockResolvedValue([]) }
})

vi.mock('../combat', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../combat')>()
  return { ...actual, getEncounter: vi.fn().mockResolvedValue({ isActive: false }) }
})

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    reconnectSession: vi.fn(),
    getAdventureRun: vi.fn(),
    submitPlayerAction: vi.fn(),
    endMyTurn: vi.fn(),
    getChatHistory: vi.fn(),
  }
})

const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)
const mockedGetRun = vi.mocked(sessionsApi.getAdventureRun)
const mockedSubmit = vi.mocked(sessionsApi.submitPlayerAction)
const mockedEndTurn = vi.mocked(sessionsApi.endMyTurn)
const mockedGetChat = vi.mocked(sessionsApi.getChatHistory)
const mockedGetRollLog = vi.mocked(diceApi.getRollLog)
const mockedListCharacters = vi.mocked(charactersApi.listCharacters)

// The session store: the player (Thorin, no character) and one other approved player.
const mockStore = {
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
      isApproved: true,
      joinedAt: '2024-01-01T10:00:00Z',
      lastSeenAt: '2024-01-01T10:00:00Z',
    },
    {
      id: 'participant-2',
      sessionId: 'session-1',
      displayName: 'Bifur',
      role: ParticipantRole.Player,
      characterId: 'char-2',
      isApproved: true,
      joinedAt: '2024-01-01T10:01:00Z',
      lastSeenAt: '2024-01-01T10:01:00Z',
    },
  ],
  loading: false,
  error: null,
  fetchSession: vi.fn().mockResolvedValue(undefined),
  fetchParticipants: vi.fn().mockResolvedValue(undefined),
}

vi.mock('../store', () => ({
  useSessionStore: vi.fn(() => mockStore),
}))

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

function tokenExpiring(expSeconds: number): string {
  return `h.${btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '')}.s`
}
const LIVE_TOKEN = tokenExpiring(Math.floor(Date.now() / 1000) + 3600)

const approved = (isApproved = true): sessionsApi.ReconnectResult => ({
  sessionId: 'session-1',
  participantId: 'participant-1',
  displayName: 'Thorin',
  role: ParticipantRole.Player,
  isApproved,
})

function makeRun(overrides: Partial<PlayerAdventureRun> = {}): PlayerAdventureRun {
  return {
    runId: 'run-1',
    adventureTitle: 'The Cellar Below',
    status: 'InProgress',
    isGroup: true,
    scene: { title: 'The Cellar', description: 'Cold stone steps lead down into the dark.' },
    currentSceneNumber: 1,
    totalScenes: 3,
    activeParticipantId: 'participant-2',
    activeParticipantName: 'Bifur',
    currentRound: 2,
    turnOrder: [
      { participantId: 'participant-2', participantName: 'Bifur', isActive: true },
      { participantId: 'participant-1', participantName: 'Thorin', isActive: false },
    ],
    pendingRoll: null,
    recentNarration: [
      { kind: 'AiNarration', text: 'A door groans somewhere below.', createdAt: '2024-01-01T10:05:00Z' },
      { kind: 'PlayerAction', text: 'I listen at the stairs.', createdAt: '2024-01-01T10:06:00Z' },
    ],
    ...overrides,
  }
}

/** The run on Thorin's turn. */
const myTurnRun = (overrides: Partial<PlayerAdventureRun> = {}) =>
  makeRun({
    activeParticipantId: 'participant-1',
    activeParticipantName: 'Thorin',
    turnOrder: [
      { participantId: 'participant-2', participantName: 'Bifur', isActive: false },
      { participantId: 'participant-1', participantName: 'Thorin', isActive: true },
    ],
    ...overrides,
  })

const routes = [
  { path: '/sessions/:sessionId/play', name: 'play-session', component: PlayerSessionView },
  { path: '/join/:sessionId', name: 'join-session', component: { template: '<div>join</div>' } },
]

/** Every mounted view, so a test cannot leave one listening to the document for the next. */
const mounted: Array<{ unmount: () => void }> = []

async function mountView() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/sessions/session-1/play')
  await router.isReady()
  const replace = vi.spyOn(router, 'replace')

  const wrapper = mount(PlayerSessionView, { global: { plugins: [pinia, router] } })
  mounted.push(wrapper)
  await flushPromises()
  return { wrapper, router, replace }
}

function rememberThorin(): void {
  saveParticipantSession({ sessionId: 'session-1', participantId: 'participant-1', displayName: 'Thorin', token: LIVE_TOKEN })
}

function setVisibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('PlayerSessionView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
    hub.handlers = {}
    hub.lifecycle = {}
    hub.builds = 0
    hub.start = vi.fn().mockResolvedValue(undefined)
    hub.invoke = vi.fn().mockResolvedValue(undefined)
    rememberThorin()
    mockedReconnect.mockResolvedValue(approved())
    mockedGetRun.mockResolvedValue(makeRun())
    mockedGetChat.mockResolvedValue({ messages: [], hasMore: false })
    mockedGetRollLog.mockResolvedValue([])
    mockedListCharacters.mockResolvedValue([])
    mockStore.fetchSession = vi.fn().mockResolvedValue(undefined)
    mockStore.fetchParticipants = vi.fn().mockResolvedValue(undefined)
  })

  afterEach(() => {
    mounted.splice(0).forEach((w) => w.unmount())
    vi.useRealTimers()
    setVisibility('visible')
  })

  // ── Getting in ────────────────────────────────────────────────────────────

  describe('resuming', () => {
    it('sends someone with no remembered login to the join page', async () => {
      sessionStorage.clear()

      const { replace } = await mountView()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' } })
      expect(mockedReconnect).not.toHaveBeenCalled()
      expect(hub.builds).toBe(0)
    })

    it('sends a participant whose token the server rejects to the join page, and forgets the token', async () => {
      mockedReconnect.mockRejectedValue(httpError(401))

      const { replace } = await mountView()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' } })
      expect(loadParticipantSession()).toBeNull()
    })

    it('sends a participant who is not approved yet to the join page, where they wait, keeping their login', async () => {
      mockedReconnect.mockResolvedValue(approved(false))

      const { replace } = await mountView()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' } })
      expect(loadParticipantSession()).not.toBeNull()
      expect(hub.builds).toBe(0)
    })

    it('sends a denied participant to the join page, which shows the denial', async () => {
      mockedReconnect.mockRejectedValue(httpError(403, { code: 'participant-denied' }))

      const { replace } = await mountView()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' } })
      expect(loadParticipantSession()).not.toBeNull()
    })

    it('sends a removed participant to the join page saying so, forgetting the login and the rejoin secret', async () => {
      getOrCreateRejoinSecret('session-1', 'Thorin')
      mockedReconnect.mockRejectedValue(httpError(403, { code: 'participant-removed' }))

      const { replace } = await mountView()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' }, query: { removed: '1' } })
      expect(loadParticipantSession()).toBeNull()
      expect(getRejoinSecret('session-1')).toBeNull()
      expect(hub.builds).toBe(0)
    })

    it('leaves the table when the hub says the host removed this player, and ignores the removal of anyone else', async () => {
      getOrCreateRejoinSecret('session-1', 'Thorin')
      const { replace } = await mountView()

      hub.handlers.ParticipantRemoved({ sessionId: 'session-1', participantId: 'someone-else', displayName: 'Bob' })
      await flushPromises()
      expect(replace).not.toHaveBeenCalled()

      hub.handlers.ParticipantRemoved({ sessionId: 'session-1', participantId: 'participant-1', displayName: 'Thorin' })
      await flushPromises()

      expect(replace).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'session-1' }, query: { removed: '1' } })
      expect(loadParticipantSession()).toBeNull()
      expect(getRejoinSecret('session-1')).toBeNull()
    })

    it('stays, and keeps trying, when the host cannot be reached', async () => {
      mockedReconnect.mockRejectedValue(new AxiosError('Network Error', 'ERR_NETWORK'))

      const { wrapper, replace } = await mountView()

      expect(replace).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('Friday Night Game')
    })
  })

  // ── Rendering ─────────────────────────────────────────────────────────────

  describe('rendering', () => {
    it('renders the session name and the player name in the header', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.text()).toContain('Friday Night Game')
      expect(wrapper.text()).toContain('Thorin')
    })

    it('renders the main sections', async () => {
      const { wrapper } = await mountView()

      for (const heading of ['Scene', 'Party', 'Character', 'Dice Tray', 'Chat']) {
        expect(wrapper.text()).toContain(heading)
      }
      expect(wrapper.text()).toContain('Waiting for character assignment')
      expect(wrapper.text()).toContain('The host will assign a character to you shortly.')
    })

    it('lists the party, you first', async () => {
      const { wrapper } = await mountView()

      const members = wrapper.findAll('[data-testid="party-list"] li').map((li) => li.text())
      expect(members[0]).toContain('Thorin (you)')
      expect(members[1]).toContain('Bifur')
    })

    it('renders the mobile tab navigation with its tabs', async () => {
      const { wrapper } = await mountView()

      const labels = wrapper.find('nav[aria-label="Session sections"]').findAll('button').map((b) => b.text().toLowerCase())
      expect(labels).toEqual(['scene', 'chat', 'dice', 'party'])
    })

    it('makes the mobile controls tappable (at least 44px) and lets the content shrink instead of overflowing', async () => {
      const { wrapper } = await mountView()

      const tabs = wrapper.find('nav[aria-label="Session sections"]').findAll('button')
      for (const tab of tabs) expect(tab.classes()).toContain('min-h-[44px]')
      expect(wrapper.find('[data-testid="action-submit"]').classes()).toContain('min-h-[44px]')
      expect(wrapper.find('[data-testid="scene-panel"]').classes()).toContain('min-w-0')
      // Inputs are 16px on a phone, so the browser does not zoom the page when one is focused.
      expect(wrapper.find('#dice-formula').classes()).toContain('text-base')
      expect(wrapper.find('#player-action').classes()).toContain('text-base')
    })

    it('renders a connection status indicator, green once the hub is up', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('span.rounded-full.bg-green-500').exists()).toBe(true)
    })

    it('renders the dice tray: shortcuts, formula input, roll button and manual roll', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.text()).toContain('d20')
      expect(wrapper.find('#dice-formula').exists()).toBe(true)
      expect(wrapper.findAll('button').some((b) => b.text() === 'Roll')).toBe(true)
      expect(wrapper.text()).toContain('Manual Roll')
    })
  })

  // ── The hub ───────────────────────────────────────────────────────────────

  describe('the hub', () => {
    it('joins the session group with the message object the hub expects, not a bare session id', async () => {
      await mountView()

      expect(hub.invoke).toHaveBeenCalledWith('JoinSession', {
        sessionId: 'session-1',
        participantId: 'participant-1',
        displayName: 'Thorin',
        role: ParticipantRole.Player,
      })
    })

    it('joins the group again after an automatic reconnect, and reads everything again', async () => {
      await mountView()
      hub.invoke.mockClear()
      mockedGetRun.mockClear()

      hub.lifecycle.reconnected!()
      await flushPromises()

      expect(hub.invoke).toHaveBeenCalledWith('JoinSession', expect.objectContaining({ sessionId: 'session-1' }))
      expect(mockedGetRun).toHaveBeenCalled()
    })

    it('shows chat messages from the notification object the hub sends', async () => {
      const { wrapper } = await mountView()

      hub.handlers.ChatMessageReceived({
        sessionId: 'session-1',
        participantId: 'participant-2',
        displayName: 'Bifur',
        text: 'Watch the stairs!',
        sentAt: '2024-01-01T10:07:00Z',
      })
      await flushPromises()

      expect(wrapper.text()).toContain('Watch the stairs!')
      expect(wrapper.text()).toContain('Bifur')
    })

    it('sends chat as the object the hub expects', async () => {
      const { wrapper } = await mountView()
      hub.invoke.mockClear()

      await wrapper.findComponent({ name: 'ChatPanel' }).vm.$emit('send', 'hello table')
      await flushPromises()

      expect(hub.invoke).toHaveBeenCalledWith('SendChatMessage', {
        sessionId: 'session-1',
        participantId: 'participant-1',
        displayName: 'Thorin',
        text: 'hello table',
      })
    })

    it('shows rolls in the roll log, from the payload the server sends ({ sessionId, roll })', async () => {
      const { wrapper } = await mountView()

      // The real RollRecorded payload: the roll wrapped with its session, the roller's name included.
      hub.handlers.RollRecorded({
        sessionId: 'session-1',
        roll: {
          id: 'roll-9',
          sessionId: 'session-1',
          characterId: null,
          rollerParticipantId: 'participant-2',
          rollerName: 'Bifur',
          formula: '1d20+2',
          dieResults: [15],
          modifier: 2,
          total: 17,
          isManual: false,
          visibility: 0,
          context: 'Perception',
          createdAt: '2024-01-01T10:08:00Z',
        },
      })
      await flushPromises()

      expect(wrapper.find('[aria-label="Roll log"]').text()).toContain('Bifur: 1d20+2 [15] +2 = 17 (Perception)')
    })

    it('reads the run again when the hub says the adventure changed, and shows the new turn', async () => {
      const { wrapper } = await mountView()
      expect(wrapper.find('[data-testid="your-turn-banner"]').exists()).toBe(false)
      mockedGetRun.mockResolvedValue(myTurnRun({ currentRound: 3 }))

      hub.handlers.AdventureStateChanged({ sessionId: 'run-1', reason: 'TurnAdvanced', changedAt: '2024-01-01T10:09:00Z' })
      await flushPromises()

      expect(wrapper.find('[data-testid="your-turn-banner"]').exists()).toBe(true)
      expect(wrapper.text()).toContain('Round 3')
    })

    it('refreshes the party when someone joins or is approved', async () => {
      await mountView()
      mockStore.fetchParticipants.mockClear()

      hub.handlers.ParticipantJoined({})
      hub.handlers.ParticipantApproved({})
      await flushPromises()

      expect(mockStore.fetchParticipants).toHaveBeenCalledTimes(2)
    })

    it('only shows a roll request that is for this player', async () => {
      const { wrapper } = await mountView()

      hub.handlers.RollRequested({ sessionId: 'session-1', targetParticipantId: 'participant-2', characterId: null, formula: '1d20', label: 'Bifur check', dc: 10, reason: null })
      await flushPromises()
      expect(wrapper.find('[data-testid="roll-prompt"]').exists()).toBe(false)

      hub.handlers.RollRequested({ sessionId: 'session-1', targetParticipantId: 'participant-1', characterId: null, formula: '1d20+3', label: 'Athletics check', dc: 12, reason: 'Climb the wall' })
      await flushPromises()
      const prompt = wrapper.find('[data-testid="roll-prompt"]')
      expect(prompt.text()).toContain('Athletics check')
      expect(prompt.text()).toContain('1d20+3')
      expect(prompt.text()).toContain('DC 12')
    })
  })

  // ── Coming back to the page ──────────────────────────────────────────────

  describe('coming back to the page', () => {
    it('reads everything again when the page becomes visible', async () => {
      await mountView()
      mockedGetRun.mockClear()
      mockStore.fetchParticipants.mockClear()
      mockedGetRun.mockResolvedValue(myTurnRun())

      setVisibility('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      await flushPromises()

      expect(mockedGetRun).toHaveBeenCalled()
      expect(mockStore.fetchParticipants).toHaveBeenCalled()
    })

    it('does nothing while the page is hidden', async () => {
      await mountView()
      mockedGetRun.mockClear()

      setVisibility('hidden')
      document.dispatchEvent(new Event('visibilitychange'))
      await flushPromises()

      expect(mockedGetRun).not.toHaveBeenCalled()
    })

    it('reads everything again when the page is restored from the back/forward cache', async () => {
      await mountView()
      mockedGetRun.mockClear()

      const event = new Event('pageshow') as PageTransitionEvent
      Object.defineProperty(event, 'persisted', { value: true })
      window.dispatchEvent(event)
      await flushPromises()

      expect(mockedGetRun).toHaveBeenCalled()
    })

    it('opens a new connection, joins the group again, and catches up, when the old one died in the background', async () => {
      await mountView()
      expect(hub.builds).toBe(1)
      hub.invoke.mockClear()
      mockedGetRun.mockClear()
      hub.lifecycle.close!() // the browser cut the connection while the app was away

      document.dispatchEvent(new Event('visibilitychange'))
      await flushPromises()

      expect(hub.builds).toBe(2)
      expect(hub.invoke).toHaveBeenCalledWith('JoinSession', expect.objectContaining({ participantId: 'participant-1' }))
      expect(mockedGetRun).toHaveBeenCalled()
    })

    it('leaves a healthy connection alone', async () => {
      await mountView()

      document.dispatchEvent(new Event('visibilitychange'))
      await flushPromises()

      expect(hub.builds).toBe(1)
    })

    it('falls back to polling quickly while the connection is down', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
      await mountView()
      hub.lifecycle.close!()
      mockedGetRun.mockClear()

      await vi.advanceTimersByTimeAsync(5_000)

      expect(mockedGetRun).toHaveBeenCalled()
    })

    it('stops listening once the player leaves the page', async () => {
      const { wrapper } = await mountView()
      wrapper.unmount()
      mockedGetRun.mockClear()

      document.dispatchEvent(new Event('visibilitychange'))
      await flushPromises()

      expect(mockedGetRun).not.toHaveBeenCalled()
    })
  })

  // ── The adventure ─────────────────────────────────────────────────────────

  describe('the adventure', () => {
    it('says the adventure has not started when there is none', async () => {
      mockedGetRun.mockResolvedValue(null)

      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="no-adventure"]').text()).toContain('has not started the adventure')
      expect(wrapper.find('[data-testid="action-reason"]').exists()).toBe(false)
    })

    it('shows the scene title, the public description, the round and the scene count', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="scene-title"]').text()).toBe('The Cellar')
      expect(wrapper.find('[data-testid="scene-description"]').text()).toContain('Cold stone steps')
      expect(wrapper.text()).toContain('Round 2')
      expect(wrapper.text()).toContain('Scene 1 of 3')
    })

    it('shows the recent narration, oldest first, with the players\' own words apart', async () => {
      const { wrapper } = await mountView()

      const lines = wrapper.findAll('[data-testid="story"] [data-kind]')
      expect(lines.map((l) => l.text())).toEqual(['A door groans somewhere below.', 'I listen at the stairs.'])
      expect(lines.map((l) => l.attributes('data-kind'))).toEqual(['AiNarration', 'PlayerAction'])
    })

    it('shows the turn order from position 1 with the acting player highlighted', async () => {
      const { wrapper } = await mountView()

      const rows = wrapper.findAll('[data-testid="turn-order"] li')
      expect(rows).toHaveLength(2)
      expect(rows[0].text()).toContain('1')
      expect(rows[0].text()).toContain('Bifur')
      expect(rows[0].attributes('data-active')).toBe('true')
      expect(rows[0].attributes('aria-current')).toBe('step')
      expect(rows[1].text()).toContain('2')
      expect(rows[1].text()).toContain('Thorin (you)')
      expect(rows[1].attributes('data-active')).toBe('false')
    })

    it('says "Your turn" in the turn order and in a banner when it is the player\'s turn', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())

      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="your-turn-banner"]').text()).toContain('Your turn')
      const active = wrapper.find('[data-testid="turn-order"] li[data-active="true"]')
      expect(active.text()).toContain('Your turn')
      expect(active.text()).toContain('Thorin (you)')
    })

    it('does not show the "Your turn" banner to someone waiting', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="your-turn-banner"]').exists()).toBe(false)
    })

    it('never shows anything DM-only, even if a response carried it', async () => {
      mockedGetRun.mockResolvedValue({
        ...makeRun(),
        privateDmNote: 'DM ONLY: the door is trapped',
        dmNotes: 'DM ONLY: the cultists wait below',
        secrets: [{ content: 'DM ONLY: a hidden lever' }],
        npcs: [{ name: 'DM ONLY NPC' }],
      } as unknown as PlayerAdventureRun)

      const { wrapper } = await mountView()

      expect(wrapper.html()).not.toContain('DM ONLY')
    })
  })

  // ── The action box ────────────────────────────────────────────────────────

  describe('the action box', () => {
    it('is disabled, and says who the table is waiting for, when it is not the player\'s turn', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('#player-action').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="action-submit"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="action-reason"]').text()).toBe('Waiting for Bifur')
    })

    it('is enabled on the player\'s turn', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())

      const { wrapper } = await mountView()

      expect(wrapper.find('#player-action').attributes('disabled')).toBeUndefined()
      expect(wrapper.find('[data-testid="action-reason"]').exists()).toBe(false)
    })

    it('keeps the send button disabled until something is typed', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      const { wrapper } = await mountView()
      expect(wrapper.find('[data-testid="action-submit"]').attributes('disabled')).toBeDefined()

      await wrapper.find('#player-action').setValue('I open the door.')

      expect(wrapper.find('[data-testid="action-submit"]').attributes('disabled')).toBeUndefined()
    })

    it('does not send anything when it is not the player\'s turn, even if the form is submitted', async () => {
      const { wrapper } = await mountView()

      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()

      expect(mockedSubmit).not.toHaveBeenCalled()
    })

    it('sends the action to the run, clears the box, and reads the run again', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      mockedSubmit.mockResolvedValue({ narration: 'The door creaks open.' })
      const { wrapper } = await mountView()
      mockedGetRun.mockClear()
      mockedGetRun.mockResolvedValue(makeRun())   // the turn has moved on

      await wrapper.find('#player-action').setValue('I open the door.')
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()

      expect(mockedSubmit).toHaveBeenCalledWith('run-1', 'I open the door.')
      expect((wrapper.find('#player-action').element as HTMLTextAreaElement).value).toBe('')
      expect(mockedGetRun).toHaveBeenCalled()
      expect(wrapper.find('[data-testid="action-reason"]').text()).toBe('Waiting for Bifur')
    })

    it('shows a loading state while the AI DM answers, and blocks a second send', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      let answer: (value: { narration: string }) => void = () => undefined
      mockedSubmit.mockReturnValue(new Promise((resolve) => (answer = resolve)))
      const { wrapper } = await mountView()

      await wrapper.find('#player-action').setValue('I open the door.')
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()

      expect(wrapper.find('[data-testid="ai-thinking"]').text()).toContain('The AI DM is thinking')
      expect(wrapper.find('[data-testid="action-submit"]').attributes('aria-busy')).toBe('true')
      expect(wrapper.find('#player-action').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="action-reason"]').text()).toContain('answering')

      answer({ narration: 'ok' })
      await flushPromises()
      expect(wrapper.find('[data-testid="ai-thinking"]').exists()).toBe(false)
    })

    it('shows the server\'s "not your turn" message, and keeps what the player typed', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      mockedSubmit.mockRejectedValue(httpError(403, { title: 'Not your turn', detail: 'It is not your turn. Waiting for Bifur.' }))
      const { wrapper } = await mountView()

      await wrapper.find('#player-action').setValue('I charge in.')
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()

      expect(wrapper.find('[data-testid="action-error"]').text()).toBe('It is not your turn. Waiting for Bifur.')
      expect((wrapper.find('#player-action').element as HTMLTextAreaElement).value).toBe('I charge in.')
    })

    it('shows a readable message for a 409 and for an AI provider failure', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      const { wrapper } = await mountView()
      await wrapper.find('#player-action').setValue('I search.')

      mockedSubmit.mockRejectedValueOnce(httpError(409, { detail: 'Another action is already being resolved.' }))
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()
      expect(wrapper.find('[data-testid="action-error"]').text()).toBe('Another action is already being resolved.')

      mockedSubmit.mockRejectedValueOnce(httpError(500))
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()
      expect(wrapper.find('[data-testid="action-error"]').text()).toContain('AI provider failed')
      expect(wrapper.text()).not.toContain('status code 500')
    })

    it('says so when the adventure is over', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun({ status: 'Completed' }))

      const { wrapper } = await mountView()

      expect(wrapper.find('#player-action').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="action-reason"]').text()).toContain('over')
    })
  })

  // ── The roll the DM is waiting for ───────────────────────────────────────

  describe('a pending roll', () => {
    const owed = () => myTurnRun({ pendingRoll: { label: 'Dexterity (Stealth) check', dc: 14 } })

    it('shows the label and DC to the player who owes it', async () => {
      mockedGetRun.mockResolvedValue(owed())

      const { wrapper } = await mountView()

      const prompt = wrapper.find('[data-testid="roll-prompt"]')
      expect(prompt.text()).toContain('Dexterity (Stealth) check')
      expect(prompt.text()).toContain('DC 14')
    })

    it('shows nothing when the server sent no pending roll (it only sends it to the player who owes it)', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="roll-prompt"]').exists()).toBe(false)
    })

    it('"Roll Now" opens the dice tray ready for it', async () => {
      mockedGetRun.mockResolvedValue(owed())
      const { wrapper } = await mountView()

      await wrapper.findAll('[data-testid="roll-prompt"] button').find((b) => b.text() === 'Roll Now')!.trigger('click')

      expect((wrapper.find('#dice-context').element as HTMLInputElement).value).toBe('Dexterity (Stealth) check')
      const tabs = wrapper.find('nav[aria-label="Session sections"]').findAll('button')
      expect(tabs.find((t) => t.text() === 'dice')!.attributes('aria-pressed')).toBe('true')
    })

    it('after rolling, offers to put the result into the action box in words, for the player to send', async () => {
      mockedGetRun.mockResolvedValue(owed())
      const { wrapper } = await mountView()
      await wrapper.findAll('[data-testid="roll-prompt"] button').find((b) => b.text() === 'Roll Now')!.trigger('click')
      await wrapper.findAll('button').find((b) => b.text() === 'Roll')!.trigger('click')
      await flushPromises()

      await wrapper.find('[data-testid="use-roll"]').trigger('click')

      expect((wrapper.find('#player-action').element as HTMLTextAreaElement).value).toBe('I rolled 15 for Dexterity (Stealth) check.')
      expect(mockedSubmit).not.toHaveBeenCalled()   // nothing is reported to the run until the player sends it
    })
  })
  // ── Ending your own turn ─────────────────────────────────────────────────

  describe('"End my turn"', () => {
    it('is an enabled button on the player\'s turn, and ends the turn when pressed', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      mockedEndTurn.mockResolvedValue(makeRun())   // the table view after the turn moved on
      const { wrapper } = await mountView()
      const button = wrapper.find('[data-testid="end-turn-button"]')
      expect(button.text()).toBe('End my turn')
      expect(button.attributes('disabled')).toBeUndefined()

      await button.trigger('click')
      await flushPromises()

      expect(mockedEndTurn).toHaveBeenCalledWith('run-1')
      // The turn is Bifur's now: the button waits for him.
      expect(wrapper.find('[data-testid="end-turn-button"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="end-turn-button"]').text()).toBe('Waiting for Bifur')
    })

    it('is disabled and says "Waiting for <name>" for everyone else', async () => {
      const { wrapper } = await mountView()

      const button = wrapper.find('[data-testid="end-turn-button"]')

      expect(button.attributes('disabled')).toBeDefined()
      expect(button.text()).toBe('Waiting for Bifur')
      await button.trigger('click')
      expect(mockedEndTurn).not.toHaveBeenCalled()
    })

    it('is replaced by a note when the run moves the turn on by itself', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun({ turnAdvance: 'AfterEachAction' }))

      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="end-turn-button"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="auto-advance-note"]').text()).toContain('by itself')
    })

    it('is not shown when there is no group adventure', async () => {
      mockedGetRun.mockResolvedValue(null)

      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="end-turn"]').exists()).toBe(false)
    })

    it('shows the server\'s message, and who has the turn now, when the turn had already moved on (403)', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      mockedEndTurn.mockRejectedValue(httpError(403, { title: 'Not your turn', detail: 'It is not your turn. Waiting for Bifur.' }))
      const { wrapper } = await mountView()
      mockedGetRun.mockResolvedValue(makeRun())   // the host passed it a moment ago

      await wrapper.find('[data-testid="end-turn-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="end-turn-error"]').text()).toBe('It is not your turn. Waiting for Bifur.')
      expect(wrapper.text()).not.toContain('status code')
      expect(wrapper.find('[data-testid="end-turn-button"]').text()).toBe('Waiting for Bifur')
    })

    it('is disabled while the AI is answering the player\'s action', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())
      mockedSubmit.mockReturnValue(new Promise(() => undefined))
      const { wrapper } = await mountView()
      await wrapper.find('#player-action').setValue('I search.')
      await wrapper.find('[data-testid="action-form"]').trigger('submit')
      await flushPromises()

      expect(wrapper.find('[data-testid="end-turn-button"]').attributes('disabled')).toBeDefined()
    })
  })

  // ── Roll log ─────────────────────────────────────────────────────────────

  describe('the roll log', () => {
    const rollBy = (id: string, overrides: Record<string, unknown> = {}) => ({
      id,
      sessionId: 'session-1',
      characterId: null,
      rollerParticipantId: 'participant-2',
      rollerName: 'Bifur',
      formula: '1d20+2',
      dieResults: [15],
      modifier: 2,
      total: 17,
      isManual: false,
      visibility: 0,
      context: 'Perception',
      createdAt: '2024-01-01T10:08:00Z',
      ...overrides,
    })

    it('loads the stored log when the page opens, with the roller\'s name, formula, dice and total', async () => {
      mockedGetRollLog.mockResolvedValue([rollBy('r1')])

      const { wrapper } = await mountView()

      expect(mockedGetRollLog).toHaveBeenCalledWith('session-1')
      expect(wrapper.find('[aria-label="Roll log"]').text()).toContain('Bifur: 1d20+2 [15] +2 = 17 (Perception)')
    })

    it('shows a public roll made on another device, live', async () => {
      const { wrapper } = await mountView()

      hub.handlers.RollRecorded({ sessionId: 'session-1', roll: rollBy('live-1', { context: 'Athletics' }) })
      await flushPromises()

      expect(wrapper.find('[aria-label="Roll log"]').text()).toContain('Bifur: 1d20+2 [15] +2 = 17 (Athletics)')
    })

    it('shows a roll once when it is in the history and arrives live too', async () => {
      mockedGetRollLog.mockResolvedValue([rollBy('r1')])
      const { wrapper } = await mountView()

      hub.handlers.RollRecorded({ sessionId: 'session-1', roll: rollBy('r1') })
      await flushPromises()

      expect(wrapper.findAll('[data-testid="roll-entry"]')).toHaveLength(1)
    })

    it('never shows someone else\'s private roll, from history or live (the server filters; so does the screen)', async () => {
      mockedGetRollLog.mockResolvedValue([rollBy('secret-1', { visibility: 1, context: 'secret insight' })])
      const { wrapper } = await mountView()

      hub.handlers.RollRecorded({ sessionId: 'session-1', roll: rollBy('secret-2', { visibility: 1, context: 'secret stealth' }) })
      await flushPromises()

      expect(wrapper.find('[aria-label="Roll log"]').text()).not.toContain('secret')
      expect(wrapper.findAll('[data-testid="roll-entry"]')).toHaveLength(0)
    })

    it('shows the player\'s own private roll, marked private, as "You"', async () => {
      const { wrapper } = await mountView()

      hub.handlers.RollRecorded({
        sessionId: 'session-1',
        roll: rollBy('mine', { visibility: 1, rollerParticipantId: 'participant-1', rollerName: 'Thorin', context: 'my secret' }),
      })
      await flushPromises()

      const entry = wrapper.find('[data-testid="roll-entry"]')
      expect(entry.attributes('data-visibility')).toBe('private')
      expect(entry.text()).toContain('You: 1d20+2')
      expect(entry.text()).toContain('private')
    })

    it('loads the history again when the dice tab is opened, and after a reconnect', async () => {
      const { wrapper } = await mountView()
      mockedGetRollLog.mockClear()
      mockedGetRollLog.mockResolvedValue([rollBy('missed')])

      await wrapper.find('nav[aria-label="Session sections"]').findAll('button').find((b) => b.text() === 'dice')!.trigger('click')
      await flushPromises()
      expect(mockedGetRollLog).toHaveBeenCalledTimes(1)
      expect(wrapper.findAll('[data-testid="roll-entry"]')).toHaveLength(1)

      mockedGetRollLog.mockClear()
      hub.lifecycle.reconnected!()
      await flushPromises()
      expect(mockedGetRollLog).toHaveBeenCalledTimes(1)
    })
  })

  // ── Chat history ─────────────────────────────────────────────────────────

  describe('chat history', () => {
    const said = (id: string, minute: number, overrides: Record<string, unknown> = {}) => ({
      id,
      sessionId: 'session-1',
      senderId: 'participant-2',
      senderName: 'Bifur',
      senderRole: 2,
      text: `line ${id}`,
      sentAt: `2024-01-01T10:${String(minute).padStart(2, '0')}:00Z`,
      ...overrides,
    })

    it('loads the stored chat when the page opens', async () => {
      mockedGetChat.mockResolvedValue({ messages: [said('a', 1), said('b', 2)], hasMore: false })

      const { wrapper } = await mountView()

      expect(mockedGetChat).toHaveBeenCalledWith('session-1')
      expect(wrapper.text()).toContain('line a')
      expect(wrapper.text()).toContain('line b')
    })

    it('shows a message once when it is in the history and arrives live too (same id)', async () => {
      mockedGetChat.mockResolvedValue({ messages: [said('a', 1)], hasMore: false })
      const { wrapper } = await mountView()

      hub.handlers.ChatMessageReceived({
        sessionId: 'session-1', participantId: 'participant-2', displayName: 'Bifur', text: 'line a',
        sentAt: '2024-01-01T10:01:00Z', messageId: 'a', role: 'Player',
      })
      await flushPromises()

      expect(wrapper.text().match(/line a/g)).toHaveLength(1)
    })

    it('loads the history again when the chat tab is opened, and after a reconnect', async () => {
      const { wrapper } = await mountView()
      mockedGetChat.mockClear()
      mockedGetChat.mockResolvedValue({ messages: [said('missed', 3)], hasMore: false })

      await wrapper.find('nav[aria-label="Session sections"]').findAll('button').find((b) => b.text() === 'chat')!.trigger('click')
      await flushPromises()
      expect(mockedGetChat).toHaveBeenCalledTimes(1)
      expect(wrapper.text()).toContain('line missed')

      mockedGetChat.mockClear()
      hub.lifecycle.reconnected!()
      await flushPromises()
      expect(mockedGetChat).toHaveBeenCalledTimes(1)
    })

    it('pages up: "Load earlier messages" asks for the page before the oldest, and puts it on top', async () => {
      mockedGetChat
        .mockResolvedValueOnce({ messages: [said('c', 3), said('d', 4)], hasMore: true })
        .mockResolvedValueOnce({ messages: [said('a', 1), said('b', 2)], hasMore: false })
      const { wrapper } = await mountView()

      await wrapper.find('[data-testid="load-older"]').trigger('click')
      await flushPromises()

      expect(mockedGetChat).toHaveBeenLastCalledWith('session-1', { before: 'c' })
      const order = wrapper.findAll('[aria-label="Chat messages"] > div').map((d) => d.text())
      expect(order.map((t) => /line (\w)/.exec(t)?.[1])).toEqual(['a', 'b', 'c', 'd'])
      expect(wrapper.find('[data-testid="load-older"]').exists()).toBe(false)   // nothing older left
    })

    it('puts the player\'s own stored messages on their side', async () => {
      mockedGetChat.mockResolvedValue({ messages: [said('mine', 1, { senderId: 'participant-1', senderName: 'Thorin' })], hasMore: false })

      const { wrapper } = await mountView()

      expect(wrapper.find('[aria-label="Chat messages"] > div').classes()).toContain('items-end')
    })

    it('says so, and keeps the live chat working, when the history cannot be loaded', async () => {
      mockedGetChat.mockRejectedValue(httpError(500))
      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="chat-history-error"]').text()).toContain('chat history')
    })
  })

  // ── Wording, character, layout ───────────────────────────────────────────

  describe('wording and layout', () => {
    it('says "Tell the GM what you do" on the player\'s turn', async () => {
      mockedGetRun.mockResolvedValue(myTurnRun())

      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="your-turn-banner"]').text()).toContain('Tell the GM what you do.')
    })

    it('shows the player\'s character by name, not by id', async () => {
      mockStore.participants[0] = { ...mockStore.participants[0], characterId: 'char-1' }
      mockedListCharacters.mockResolvedValue([{ id: 'char-1', name: 'Thorin Stonehelm' }])
      try {
        const { wrapper } = await mountView()

        expect(wrapper.find('[data-testid="my-character"]').text()).toContain('Thorin Stonehelm')
        expect(wrapper.text()).not.toContain('char-1')
      } finally {
        mockStore.participants[0] = { ...mockStore.participants[0], characterId: null }
      }
    })

    it('keeps the tab bar flush with the top of the page (its own class, not overlapping the content)', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('nav[aria-label="Session sections"]').classes()).toContain('session-tabs')
    })

    it('lets the story grow with the page instead of scrolling in a box of its own', async () => {
      const { wrapper } = await mountView()

      const story = wrapper.find('[data-testid="story"]')
      expect(story.classes().join(' ')).not.toMatch(/max-h|overflow-y/)
    })

    it('gives the story entries room above them, so the newest one scrolls clear of the tab bar', async () => {
      const { wrapper } = await mountView()

      expect(wrapper.find('[data-testid="story"] [data-kind]').classes()).toContain('scroll-mt-24')
    })

    it('brings a new story entry into view when one arrives, but not on the first load', async () => {
      const scrollIntoView = vi.fn()
      Element.prototype.scrollIntoView = scrollIntoView
      const { wrapper } = await mountView()
      expect(scrollIntoView).not.toHaveBeenCalled()
      mockedGetRun.mockResolvedValue(makeRun({
        recentNarration: [...makeRun().recentNarration, { kind: 'AiNarration', text: 'A new sound.', createdAt: '2024-01-01T10:20:00Z' }],
      }))

      hub.handlers.AdventureStateChanged({ sessionId: 'run-1', reason: 'PlayerAction', changedAt: '2024-01-01T10:20:00Z' })
      await flushPromises()

      expect(wrapper.text()).toContain('A new sound.')
      expect(scrollIntoView).toHaveBeenCalled()
    })

    it('starts a tab at its top, so it never opens with its first lines behind the tab bar', async () => {
      const scrollTo = vi.fn()
      const { wrapper } = await mountView()
      const main = document.createElement('main')
      main.style.overflowY = 'auto'
      main.scrollTo = scrollTo as unknown as typeof main.scrollTo
      document.body.appendChild(main)
      main.appendChild(wrapper.element)

      await wrapper.find('nav[aria-label="Session sections"]').findAll('button').find((b) => b.text() === 'party')!.trigger('click')
      await flushPromises()

      expect(scrollTo).toHaveBeenCalledWith({ top: 0 })
      main.remove()
    })
  })
})

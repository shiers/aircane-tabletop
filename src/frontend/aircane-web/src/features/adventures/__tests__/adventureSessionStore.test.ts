import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAdventureSessionStore } from '../stores/adventureSessionStore'
import {
  makeState,
  makeScene,
  makeNpc,
  later,
  RUN_ID,
  PLAY_SESSION_ID,
} from './fixtures'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  reconnected: null as null | (() => Promise<void> | void),
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

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    startAdventureSession: vi.fn(),
    getAdventureSession: vi.fn(),
    submitAdventureAction: vi.fn(),
    advanceAdventureScene: vi.fn(),
    abandonAdventureSession: vi.fn(),
  }
})

import * as api from '../api'

const mocked = vi.mocked(api)

/** Fire a server→client SignalR event the way the hub would. */
async function emit(name: string, payload: unknown): Promise<void> {
  const handler = hub.handlers.get(name)
  if (!handler) throw new Error(`No handler registered for ${name}`)
  handler(payload)
  // The handler refreshes asynchronously; let the promise chain settle.
  await new Promise((r) => setTimeout(r, 0))
}

function entries(store: ReturnType<typeof useAdventureSessionStore>): string[] {
  return store.narrationHistory.map((e) => `${e.kind}: ${e.text}`)
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  hub.handlers.clear()
  hub.reconnected = null
  hub.connection.on.mockImplementation((name: string, cb: (...args: unknown[]) => void) => {
    hub.handlers.set(name, cb)
  })
  hub.connection.onreconnected.mockImplementation((cb: () => Promise<void> | void) => {
    hub.reconnected = cb
  })
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
})

// ---------------------------------------------------------------------------
// startSession / loadSession
// ---------------------------------------------------------------------------

describe('AdventureSessionStore', () => {
  it('AdventureSessionStore_StartSession_SetsCurrentSession', async () => {
    const state = makeState()
    mocked.startAdventureSession.mockResolvedValue({ id: RUN_ID } as never)
    mocked.getAdventureSession.mockResolvedValue(state)
    const store = useAdventureSessionStore()

    const id = await store.startSession('adventure-1', 'campaign-1')

    expect(id).toBe(RUN_ID)
    expect(mocked.startAdventureSession).toHaveBeenCalledWith({
      generatedAdventureId: 'adventure-1',
      campaignId: 'campaign-1',
      characterId: undefined,
    })
    expect(store.currentSession).toEqual(state)
    expect(store.isLoading).toBe(false)
    expect(store.error).toBeNull()
  })

  it('startSession surfaces the server detail and rethrows when the adventure cannot start', async () => {
    mocked.startAdventureSession.mockRejectedValue(
      Object.assign(new Error('Request failed'), {
        isAxiosError: true,
        response: { status: 409, data: { detail: 'This campaign already has an adventure in progress.' } },
      }),
    )
    const store = useAdventureSessionStore()

    await expect(store.startSession('adventure-1', 'campaign-1')).rejects.toThrow()

    expect(store.error).toBe('This campaign already has an adventure in progress.')
    expect(store.currentSession).toBeNull()
    expect(store.isLoading).toBe(false)
  })

  it('loadSession seeds the feed with the scene read-aloud text once', async () => {
    mocked.getAdventureSession.mockResolvedValue(makeState())
    const store = useAdventureSessionStore()

    await store.loadSession(RUN_ID)
    await store.loadSession(RUN_ID)

    expect(entries(store)).toEqual(['dm: Cold water laps at your boots.'])
  })

  // -------------------------------------------------------------------------
  // sendPlayerAction
  // -------------------------------------------------------------------------

  it('sendPlayerAction shows the action and the DM narration, and adopts the refreshed state', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    const next = later(
      makeState({ revealedClues: [{ id: 'clue-1', name: 'The torn letter', description: 'Meet me below.' }] }),
    )
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'You find a torn letter.',
      privateDmNote: null,
      actions: [{ actionType: 'RevealClue', outcome: 'Applied', errorMessage: null }],
      state: next,
    })

    await store.sendPlayerAction('  I search the shelves  ')

    expect(mocked.submitAdventureAction).toHaveBeenCalledWith(RUN_ID, 'I search the shelves')
    expect(entries(store)).toEqual([
      'player: I search the shelves',
      'dm: You find a torn letter.',
      'system: Clue found: The torn letter',
    ])
    expect(store.currentSession?.revealedClues).toHaveLength(1)
    expect(store.isLoading).toBe(false)
  })

  it('sendPlayerAction tells the player when the AI tried an action the backend refused', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'The way forward opens.',
      privateDmNote: null,
      actions: [
        { actionType: 'AdvanceScene', outcome: 'PermissionDenied', errorMessage: 'AI authority does not allow it.' },
      ],
      state: later(makeState()),
    })

    await store.sendPlayerAction('I open the door')

    expect(entries(store)).toContain(
      "system: The AI DM's AdvanceScene was not applied: AI authority does not allow it.",
    )
  })

  it('sendPlayerAction ignores blank text and does nothing without a run', async () => {
    const store = useAdventureSessionStore()

    await store.sendPlayerAction('hello')
    store.currentSession = makeState()
    await store.sendPlayerAction('   ')

    expect(mocked.submitAdventureAction).not.toHaveBeenCalled()
    expect(store.narrationHistory).toHaveLength(0)
  })

  it('sendPlayerAction keeps the player line, records the error, and rethrows when the AI fails', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    mocked.submitAdventureAction.mockRejectedValue(new Error('boom'))

    await expect(store.sendPlayerAction('I wait')).rejects.toThrow('boom')

    expect(entries(store)).toEqual(['player: I wait'])
    expect(store.error).toBe('boom')
    expect(store.isLoading).toBe(false)
  })

  // -------------------------------------------------------------------------
  // advanceScene
  // -------------------------------------------------------------------------

  it('advanceScene asks the AI to narrate the transition and accepts the scene change it makes', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    const hall = makeScene({ sceneId: 'scene-3', title: 'The Grand Hall', exits: [], npcs: [] })
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'You step into the hall.',
      privateDmNote: null,
      actions: [{ actionType: 'AdvanceScene', outcome: 'Applied', errorMessage: null }],
      state: later(makeState({ currentScene: hall, currentSceneNumber: 3 })),
    })

    const advanced = await store.advanceScene('scene-3')

    expect(advanced).toBe(true)
    expect(mocked.submitAdventureAction).toHaveBeenCalledWith(RUN_ID, expect.stringContaining('The Grand Hall'))
    expect(mocked.advanceAdventureScene).not.toHaveBeenCalled()
    expect(store.currentSession?.currentScene.sceneId).toBe('scene-3')
    expect(entries(store)).toContain('system: — The Grand Hall —')
  })

  it('advanceScene falls back to a host override when the AI agreed but the backend refused to apply it', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'Off you go.',
      privateDmNote: null,
      actions: [{ actionType: 'AdvanceScene', outcome: 'PermissionDenied', errorMessage: 'Needs Full Session Control.' }],
      state: later(makeState()),
    })
    const hall = makeScene({ sceneId: 'scene-3', title: 'The Grand Hall', exits: [], npcs: [] })
    mocked.advanceAdventureScene.mockResolvedValue(
      later(makeState({ currentScene: hall, currentSceneNumber: 3 }), 20),
    )

    const advanced = await store.advanceScene('scene-3')

    expect(advanced).toBe(true)
    expect(mocked.advanceAdventureScene).toHaveBeenCalledWith(RUN_ID, 'scene-3')
    expect(store.currentSession?.currentScene.sceneId).toBe('scene-3')
  })

  it('advanceScene leaves the player in place when the AI did not propose moving on', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'You have not found the tome yet.',
      privateDmNote: null,
      actions: [],
      state: later(makeState()),
    })

    const advanced = await store.advanceScene('scene-3')

    expect(advanced).toBe(false)
    expect(mocked.advanceAdventureScene).not.toHaveBeenCalled()
    expect(store.currentSession?.currentScene.sceneId).toBe('scene-2')
  })

  // -------------------------------------------------------------------------
  // abandon
  // -------------------------------------------------------------------------

  it('abandonSession calls the API and adopts the abandoned state', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    mocked.abandonAdventureSession.mockResolvedValue(later(makeState({ status: 'Abandoned' })))

    await store.abandonSession()

    expect(mocked.abandonAdventureSession).toHaveBeenCalledWith(RUN_ID)
    expect(store.currentSession?.status).toBe('Abandoned')
  })

  // -------------------------------------------------------------------------
  // State application
  // -------------------------------------------------------------------------

  it('applying the same state twice adds no duplicate feed lines', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    const next = later(makeState({ currentScene: makeScene({ sceneId: 'scene-3', title: 'The Grand Hall' }) }))
    mocked.submitAdventureAction.mockResolvedValue({ narration: 'On.', privateDmNote: null, actions: [], state: next })
    mocked.getAdventureSession.mockResolvedValue(next)

    await store.sendPlayerAction('go')
    await store.connectHub()
    await emit('AdventureSceneAdvanced', { sessionId: RUN_ID, previousSceneId: 'scene-2', newSceneId: 'scene-3', narration: null })

    expect(entries(store).filter((e) => e === 'system: — The Grand Hall —')).toHaveLength(1)
  })

  it('a stale state (older lastActivityAt) does not overwrite a newer one', async () => {
    const store = useAdventureSessionStore()
    const newer = later(makeState({ currentScene: makeScene({ sceneId: 'scene-3', title: 'The Grand Hall' }) }), 30)
    store.currentSession = newer
    mocked.getAdventureSession.mockResolvedValue(makeState()) // older
    await store.connectHub()

    await emit('AdventureClueRevealed', { sessionId: RUN_ID, clueId: 'clue-1', clueName: 'x', description: 'y' })

    expect(store.currentSession?.currentScene.sceneId).toBe('scene-3')
  })

  // -------------------------------------------------------------------------
  // SignalR
  // -------------------------------------------------------------------------

  it('AdventureSessionStore_SignalREvent_AdventureSceneAdvanced_UpdatesScene', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    const hall = makeScene({ sceneId: 'scene-3', title: 'The Grand Hall', exits: [], npcs: [] })
    mocked.getAdventureSession.mockResolvedValue(later(makeState({ currentScene: hall, currentSceneNumber: 3 })))
    await store.connectHub()

    await emit('AdventureSceneAdvanced', {
      sessionId: RUN_ID,
      previousSceneId: 'scene-2',
      newSceneId: 'scene-3',
      narration: 'You wade into the hall.',
    })

    expect(mocked.getAdventureSession).toHaveBeenCalledWith(RUN_ID)
    expect(store.currentSession?.currentScene.sceneId).toBe('scene-3')
    expect(store.currentSession?.currentScene.title).toBe('The Grand Hall')
    expect(entries(store)).toEqual(['dm: You wade into the hall.', 'system: — The Grand Hall —'])
  })

  it('SignalR clue / secret / NPC / completion events each refresh the state and log what changed', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    await store.connectHub()

    mocked.getAdventureSession.mockResolvedValueOnce(
      later(makeState({ revealedClues: [{ id: 'clue-1', name: 'The torn letter', description: '' }] }), 10),
    )
    await emit('AdventureClueRevealed', { sessionId: RUN_ID, clueId: 'clue-1', clueName: 'The torn letter', description: '' })

    mocked.getAdventureSession.mockResolvedValueOnce(
      later(
        makeState({
          revealedClues: [{ id: 'clue-1', name: 'The torn letter', description: '' }],
          revealedSecrets: [{ id: 'secret-1', name: 'The Vault Key', description: '' }],
        }),
        20,
      ),
    )
    await emit('AdventureSecretRevealed', { sessionId: RUN_ID, secretId: 'secret-1', secretName: 'The Vault Key', description: '' })

    const slain = makeScene({ npcs: [makeNpc({ isAlive: false, hasBeenMet: true })] })
    mocked.getAdventureSession.mockResolvedValueOnce(
      later(
        makeState({
          currentScene: slain,
          revealedClues: [{ id: 'clue-1', name: 'The torn letter', description: '' }],
          revealedSecrets: [{ id: 'secret-1', name: 'The Vault Key', description: '' }],
        }),
        30,
      ),
    )
    await emit('AdventureNpcUpdated', { sessionId: RUN_ID, npcId: 'npc-guard-captain', npcName: 'Guard Captain', hasBeenMet: true, isAlive: false })

    mocked.getAdventureSession.mockResolvedValueOnce(
      later(makeState({ currentScene: slain, status: 'Completed', outcomeNotes: 'You won.' }), 40),
    )
    await emit('AdventureCompleted', { sessionId: RUN_ID, outcomeNotes: 'You won.' })

    expect(entries(store)).toEqual([
      'system: Clue found: The torn letter',
      'system: Secret uncovered: The Vault Key',
      'system: Guard Captain has died.',
      'system: Adventure complete: You won.',
    ])
    expect(store.currentSession?.status).toBe('Completed')
  })

  it('SignalR events for a different run are ignored', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    await store.connectHub()

    await emit('AdventureSceneAdvanced', { sessionId: 'some-other-run', previousSceneId: 'a', newSceneId: 'b', narration: 'nope' })

    expect(mocked.getAdventureSession).not.toHaveBeenCalled()
    expect(store.narrationHistory).toHaveLength(0)
  })

  it('connectHub joins the run\'s play-session group and registers all five adventure events', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()

    await store.connectHub()

    expect([...hub.handlers.keys()].sort()).toEqual([
      'AdventureClueRevealed',
      'AdventureCompleted',
      'AdventureNpcUpdated',
      'AdventureSceneAdvanced',
      'AdventureSecretRevealed',
    ])
    expect(hub.connection.start).toHaveBeenCalledTimes(1)
    expect(hub.connection.invoke).toHaveBeenCalledWith(
      'JoinSession',
      expect.objectContaining({ sessionId: PLAY_SESSION_ID, displayName: 'Solo Player', role: 0 }),
    )
  })

  it('connectHub is a no-op for a run without a play session, and a second call does not reconnect', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState({ sessionId: null })
    await store.connectHub()
    expect(hub.connection.start).not.toHaveBeenCalled()

    store.currentSession = makeState()
    await store.connectHub()
    await store.connectHub()
    expect(hub.connection.start).toHaveBeenCalledTimes(1)
  })

  it('a failed hub connection is not fatal', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    hub.connection.start.mockRejectedValue(new Error('offline'))

    await expect(store.connectHub()).resolves.toBeUndefined()

    expect(store.error).toBeNull()
  })

  it('reconnecting re-joins the group and catches up on missed changes', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    await store.connectHub()
    hub.connection.invoke.mockClear()
    mocked.getAdventureSession.mockResolvedValue(later(makeState(), 5))

    await hub.reconnected?.()

    expect(hub.connection.invoke).toHaveBeenCalledWith('JoinSession', expect.objectContaining({ sessionId: PLAY_SESSION_ID }))
    expect(mocked.getAdventureSession).toHaveBeenCalledWith(RUN_ID)
  })

  it('disconnectHub stops the connection', async () => {
    const store = useAdventureSessionStore()
    store.currentSession = makeState()
    await store.connectHub()

    await store.disconnectHub()

    expect(hub.connection.stop).toHaveBeenCalledTimes(1)
  })
})

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import AdventureSoloSession from '../AdventureSoloSession.vue'
import { useAdventureSessionStore } from '../stores/adventureSessionStore'
import { makeState, makeScene, makeNpc, later, RUN_ID } from './fixtures'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const hub = vi.hoisted(() => ({
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
    getAdventureSession: vi.fn(),
    getAdventureHistory: vi.fn(),
    submitAdventureAction: vi.fn(),
    advanceAdventureScene: vi.fn(),
    abandonAdventureSession: vi.fn(),
  }
})

import * as api from '../api'

const mocked = vi.mocked(api)

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async function mountView(state = makeState()) {
  mocked.getAdventureSession.mockResolvedValue(state)

  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/campaigns', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()

  const wrapper = mount(AdventureSoloSession, {
    props: { sessionId: RUN_ID },
    global: { plugins: [pinia, router] },
  })
  await flushPromises()

  return { wrapper, router, store: useAdventureSessionStore(pinia) }
}

const entry = (entryType: 'PlayerAction' | 'AiNarration', content: string, n: number) => ({
  id: `e${n}`, entryType, content, sceneId: 'scene-2', createdAt: `2026-10-12T10:0${n}:00Z`,
})

const text = (wrapper: ReturnType<typeof mount>, testId: string) =>
  wrapper.findAll(`[data-testid="${testId}"]`).map((w) => w.text())

beforeEach(() => {
  vi.clearAllMocks()
  mocked.getAdventureHistory.mockResolvedValue([])
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// ---------------------------------------------------------------------------
// Rendering
// ---------------------------------------------------------------------------

describe('AdventureSoloSession', () => {
  it('AdventureSoloSession_RendersCurrentSceneInfo', async () => {
    const { wrapper } = await mountView()

    expect(text(wrapper, 'adventure-title')).toEqual(['The Sunken Archive'])
    expect(text(wrapper, 'scene-progress')).toEqual(['Scene 2 of 7'])
    expect(text(wrapper, 'scene-title')).toEqual(['The Sunken Library'])
    expect(text(wrapper, 'objective')).toEqual(['• Find the missing tome', '• Deal with the guard'])
    expect(text(wrapper, 'exit-button')).toEqual(['→ The Grand Hall', '→ The Hidden Passage'])
    // The opening narration is the scene's read-aloud text.
    expect(text(wrapper, 'narration-entry')).toEqual(['Cold water laps at your boots.'])
  })

  // ── Resuming a saved run ──────────────────────────────────────────────────

  it('AdventureSoloSession_LoadsHistory_OnMount', async () => {
    mocked.getAdventureHistory.mockResolvedValue([
      entry('PlayerAction', 'I search the shelves', 1),
      entry('AiNarration', 'You find a torn letter.', 2),
    ])

    const { wrapper } = await mountView()

    expect(mocked.getAdventureHistory).toHaveBeenCalledWith(RUN_ID)
    const feed = wrapper.findAll('[data-testid="narration-entry"]')
    expect(feed.map((e) => e.text())).toEqual(['I search the shelves', 'You find a torn letter.'])
    expect(feed.map((e) => e.attributes('data-kind'))).toEqual(['player', 'dm'])
  })

  it('AdventureSoloSession_ShowsResumeSeparator_WhenHistoryLoaded', async () => {
    mocked.getAdventureHistory.mockResolvedValue([entry('AiNarration', 'You find a torn letter.', 1)])

    const { wrapper } = await mountView()

    const separator = wrapper.find('[data-testid="resume-separator"]')
    expect(separator.text()).toBe('── Resuming session ──')
    // After the saved lines, before anything new.
    const kinds = wrapper.findAll('[data-testid="narration-feed"] > p').map((p) => p.attributes('data-kind'))
    expect(kinds).toEqual(['dm', 'separator'])
  })

  it('AdventureSoloSession_ScrollsToBottom_AfterHistoryLoad', async () => {
    const scrollHeight = vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(1234)
    mocked.getAdventureHistory.mockResolvedValue([
      entry('PlayerAction', 'first', 1),
      entry('AiNarration', 'last', 2),
    ])

    const { wrapper } = await mountView()
    await flushPromises()

    expect((wrapper.find('[data-testid="narration-feed"]').element as HTMLElement).scrollTop).toBe(1234)
    scrollHeight.mockRestore()
  })

  it('a brand-new run (no saved history) starts with the scene opening and no resume divider', async () => {
    const { wrapper } = await mountView()

    expect(mocked.getAdventureHistory).toHaveBeenCalledTimes(1)
    expect(text(wrapper, 'narration-entry')).toEqual(['Cold water laps at your boots.'])
    expect(wrapper.find('[data-testid="resume-separator"]').exists()).toBe(false)
  })

  it('still plays when the saved history cannot be loaded', async () => {
    mocked.getAdventureHistory.mockRejectedValue(new Error('history down'))

    const { wrapper } = await mountView()

    expect(text(wrapper, 'narration-entry')).toEqual(['Cold water laps at your boots.'])
    expect(wrapper.find('[data-testid="error-banner"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="action-input"]').attributes('disabled')).toBeUndefined()
  })

  it('does not reload the history when the feed already has entries (navigating back within the app)', async () => {
    const { store } = await mountView()
    expect(mocked.getAdventureHistory).toHaveBeenCalledTimes(1)

    mocked.getAdventureSession.mockResolvedValue(makeState())
    await store.openSession(RUN_ID)

    expect(mocked.getAdventureHistory).toHaveBeenCalledTimes(1)
  })

  it('AdventureSoloSession_ShowsNpcStates', async () => {
    const { wrapper } = await mountView(
      makeState({
        currentScene: makeScene({
          npcs: [
            makeNpc(),
            makeNpc({ npcId: 'npc-archivist', npcName: 'Archivist', hasBeenMet: true }),
            makeNpc({ npcId: 'npc-thug', npcName: 'Thug', isAlive: false, hasBeenMet: true }),
          ],
        }),
      }),
    )

    const rows = text(wrapper, 'npc-row')
    expect(rows).toHaveLength(3)
    expect(rows[0]).toContain('Guard Captain')
    expect(rows[0]).toContain('(alive)')
    expect(rows[1]).toContain('Archivist')
    expect(rows[1]).toContain('(met)')
    expect(rows[2]).toContain('Thug')
    expect(rows[2]).toContain('(dead)')
  })

  it('AdventureSoloSession_ShowsRevealedClues', async () => {
    const { wrapper } = await mountView(
      makeState({
        revealedClues: [{ id: 'clue-1', name: 'The torn letter', description: 'Meet me below the east stair.' }],
        revealedSecrets: [{ id: 'secret-1', name: 'The Vault Key', description: 'In the fountain.' }],
      }),
    )

    expect(text(wrapper, 'clue-item')).toEqual(['• The torn letter'])
    expect(text(wrapper, 'secret-item')).toEqual(['• The Vault Key'])
    expect(wrapper.text()).not.toContain('Nothing yet.')
  })

  it('shows "Nothing yet." and "No exits" empty states', async () => {
    const { wrapper } = await mountView(makeState({ currentScene: makeScene({ exits: [], npcs: [], objectives: [] }) }))

    expect(wrapper.text()).toContain('Nothing yet.')
    expect(wrapper.text()).toContain('No exits from here.')
    expect(wrapper.text()).toContain('Nobody in sight.')
    expect(wrapper.find('[data-testid="objective"]').exists()).toBe(false)
  })

  it('connects the hub for the run on mount and disconnects on unmount', async () => {
    const { wrapper } = await mountView()

    expect(hub.connection.start).toHaveBeenCalledTimes(1)
    expect(hub.connection.invoke).toHaveBeenCalledWith('JoinSession', expect.objectContaining({ sessionId: 'play-session-1' }))

    wrapper.unmount()
    await flushPromises()
    expect(hub.connection.stop).toHaveBeenCalledTimes(1)
  })

  it('shows the error banner when the run cannot be loaded', async () => {
    mocked.getAdventureSession.mockRejectedValue(new Error('Adventure session not found.'))
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
    const wrapper = mount(AdventureSoloSession, { props: { sessionId: 'missing' }, global: { plugins: [pinia, router] } })
    await flushPromises()

    expect(wrapper.find('[data-testid="error-banner"]').text()).toContain('Adventure session not found.')
    expect(hub.connection.start).not.toHaveBeenCalled()
  })

  // -------------------------------------------------------------------------
  // Player actions
  // -------------------------------------------------------------------------

  it('sending an action calls the AI DM and shows the exchange; the input is cleared', async () => {
    const { wrapper } = await mountView()
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'You find a torn letter.',
      privateDmNote: 'The tome is behind the third shelf.',
      actions: [],
      state: later(makeState()),
    })

    await wrapper.find('[data-testid="action-input"]').setValue('I search the shelves')
    await wrapper.find('[data-testid="action-form"]').trigger('submit')
    await flushPromises()

    expect(mocked.submitAdventureAction).toHaveBeenCalledWith(RUN_ID, 'I search the shelves')
    const feed = text(wrapper, 'narration-entry')
    expect(feed).toContain('I search the shelves')
    expect(feed).toContain('You find a torn letter.')
    expect((wrapper.find('[data-testid="action-input"]').element as HTMLInputElement).value).toBe('')
    // The DM's private note is never shown to the player.
    expect(wrapper.text()).not.toContain('third shelf')
  })

  it('Send is disabled until there is text, and the input is restored if the AI call fails', async () => {
    const { wrapper } = await mountView()
    const send = wrapper.find('[data-testid="send-button"]')
    expect(send.attributes('disabled')).toBeDefined()

    mocked.submitAdventureAction.mockRejectedValue(new Error('The AI DM could not respond.'))
    await wrapper.find('[data-testid="action-input"]').setValue('I wait')
    expect(send.attributes('disabled')).toBeUndefined()
    await wrapper.find('[data-testid="action-form"]').trigger('submit')
    await flushPromises()

    expect((wrapper.find('[data-testid="action-input"]').element as HTMLInputElement).value).toBe('I wait')
    expect(wrapper.find('[data-testid="error-banner"]').text()).toContain('The AI DM could not respond.')
  })

  // -------------------------------------------------------------------------
  // Exits
  // -------------------------------------------------------------------------

  it('AdventureSoloSession_ExitClick_CallsAdvanceScene', async () => {
    const { wrapper, store } = await mountView()
    const advance = vi.spyOn(store, 'advanceScene').mockResolvedValue(true)

    await wrapper.findAll('[data-testid="exit-button"]')[1].trigger('click')

    expect(advance).toHaveBeenCalledTimes(1)
    expect(advance).toHaveBeenCalledWith('scene-4')
  })

  it('clicking an exit asks the AI DM to narrate the move and shows the new scene', async () => {
    const { wrapper } = await mountView()
    const hall = makeScene({ sceneId: 'scene-3', title: 'The Grand Hall', exits: [], npcs: [], objectives: [] })
    mocked.submitAdventureAction.mockResolvedValue({
      narration: 'You step into the hall.',
      privateDmNote: null,
      actions: [{ actionType: 'AdvanceScene', outcome: 'Applied', errorMessage: null }],
      state: later(makeState({ currentScene: hall, currentSceneNumber: 3 })),
    })

    await wrapper.findAll('[data-testid="exit-button"]')[0].trigger('click')
    await flushPromises()

    expect(mocked.submitAdventureAction).toHaveBeenCalledWith(RUN_ID, expect.stringContaining('The Grand Hall'))
    expect(text(wrapper, 'scene-title')).toEqual(['The Grand Hall'])
    expect(text(wrapper, 'scene-progress')).toEqual(['Scene 3 of 7'])
    expect(text(wrapper, 'narration-entry')).toContain('You step into the hall.')
  })

  // -------------------------------------------------------------------------
  // Abandon
  // -------------------------------------------------------------------------

  it('Abandon asks for confirmation, then abandons the run and leaves', async () => {
    const { wrapper, router } = await mountView()
    vi.stubGlobal('confirm', vi.fn(() => true))
    mocked.abandonAdventureSession.mockResolvedValue(later(makeState({ status: 'Abandoned' })))

    await wrapper.find('[data-testid="abandon-button"]').trigger('click')
    await flushPromises()

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(mocked.abandonAdventureSession).toHaveBeenCalledWith(RUN_ID)
    expect(router.currentRoute.value.path).toBe('/campaigns')
  })

  it('Abandon does nothing when the confirmation is declined', async () => {
    const { wrapper, router } = await mountView()
    vi.stubGlobal('confirm', vi.fn(() => false))

    await wrapper.find('[data-testid="abandon-button"]').trigger('click')
    await flushPromises()

    expect(mocked.abandonAdventureSession).not.toHaveBeenCalled()
    expect(router.currentRoute.value.path).toBe('/')
  })

  // -------------------------------------------------------------------------
  // Finished runs
  // -------------------------------------------------------------------------

  it('a completed adventure shows the outcome and disables the input, exits and abandon', async () => {
    const { wrapper } = await mountView(makeState({ status: 'Completed', outcomeNotes: 'You recovered the tome.' }))

    expect(wrapper.find('[data-testid="completed-banner"]').text()).toContain('You recovered the tome.')
    expect(wrapper.find('[data-testid="action-input"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="send-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="exit-button"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="abandon-button"]').exists()).toBe(false)
  })
})

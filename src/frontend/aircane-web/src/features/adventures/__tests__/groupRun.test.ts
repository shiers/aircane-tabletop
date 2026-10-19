import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import GroupRunHostPanel from '../components/GroupRunHostPanel.vue'
import { useGroupRunStore } from '../stores/groupRunStore'
import { ParticipantRole, type ParticipantDto } from '@/features/sessions/api'
import * as api from '../api'
import { groupRunState, makeScene } from './fixtures'

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    getActiveAdventureSession: vi.fn(),
    getAdventureSession: vi.fn(),
    advanceAdventureTurn: vi.fn(),
    advanceAdventureScene: vi.fn(),
    abandonAdventureSession: vi.fn(),
    addAdventureParticipant: vi.fn(),
  }
})

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

const late = (id: string, name: string): ParticipantDto => ({
  id,
  sessionId: 'play-session-1',
  displayName: name,
  role: ParticipantRole.Player,
  characterId: null,
  isApproved: true,
  joinedAt: '2026-10-01T10:00:00Z',
  lastSeenAt: '2026-10-01T10:00:00Z',
})

function setup(run = groupRunState(), latePlayers: ParticipantDto[] = []) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useGroupRunStore()
  store.setRun(run)
  const wrapper = mount(GroupRunHostPanel, { props: { latePlayers }, global: { plugins: [pinia] } })
  return { wrapper, store }
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

describe('GroupRunHostPanel', () => {
  it('shows the scene, the round, the acting player and the turn order', () => {
    const { wrapper } = setup(groupRunState({ currentRound: 2, activeParticipantName: 'Bob' }))

    expect(wrapper.find('#group-run-heading').text()).toBe('The Sunken Archive')
    expect(wrapper.find('[data-testid="run-progress"]').text()).toBe('Scene 2 of 7 · Round 2')
    expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Bob')
    const items = wrapper.findAll('[data-testid="run-turn-order"] li')
    const cells = (li: (typeof items)[number]) => li.findAll('span').map((span) => span.text())
    expect(items.map(cells)).toEqual([['1', 'Alice'], ['2', 'Bob', 'acting'], ['3', 'Cara']])
    expect(items[1].attributes('aria-current')).toBe('step')
  })

  it('numbers the turn order from 1 (the server positions are zero-based)', () => {
    const { wrapper } = setup()

    expect(wrapper.findAll('[data-testid="run-turn-order"] li')[0].findAll('span')[0].text()).toBe('1')
  })

  describe('pass the turn', () => {
    it('makes the next player obvious on the button', () => {
      const { wrapper } = setup()

      expect(wrapper.find('[data-testid="advance-turn-button"]').text()).toContain('Pass the turn to Bob')
    })

    it('wraps to the first player after the last, and says it starts a new round', () => {
      const { wrapper } = setup(groupRunState({ activeParticipantName: 'Cara', currentRound: 3 }))

      expect(wrapper.find('[data-testid="advance-turn-button"]').text()).toContain('Pass the turn to Alice')
      expect(wrapper.find('[data-testid="new-round-hint"]').text()).toContain('round 4')
    })

    it('does not mention a new round mid-order', () => {
      const { wrapper } = setup()

      expect(wrapper.find('[data-testid="new-round-hint"]').exists()).toBe(false)
    })

    it('explains that a disconnected player keeps the turn until the host passes it', () => {
      const { wrapper } = setup()

      expect(wrapper.find('[data-testid="turn-hint"]').text()).toMatch(/disconnected/i)
      expect(wrapper.find('[data-testid="turn-hint"]').text()).toMatch(/until you do/i)
    })

    it('calls the server and shows the new acting player', async () => {
      vi.mocked(api.advanceAdventureTurn).mockResolvedValue(groupRunState({ activeParticipantName: 'Bob' }))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="advance-turn-button"]').trigger('click')
      await flushPromises()

      expect(api.advanceAdventureTurn).toHaveBeenCalledWith('run-1')
      expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Bob')
    })

    it('is disabled while passing, so a double click passes once', async () => {
      let finish!: (value: ReturnType<typeof groupRunState>) => void
      vi.mocked(api.advanceAdventureTurn).mockReturnValue(new Promise((resolve) => (finish = resolve)))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="advance-turn-button"]').trigger('click')
      expect(wrapper.find('[data-testid="advance-turn-button"]').attributes('disabled')).toBeDefined()
      await wrapper.find('[data-testid="advance-turn-button"]').trigger('click')
      finish(groupRunState({ activeParticipantName: 'Bob' }))
      await flushPromises()

      expect(api.advanceAdventureTurn).toHaveBeenCalledTimes(1)
    })

    it('shows the host-only message for a 403 and keeps the turn where it was', async () => {
      vi.mocked(api.advanceAdventureTurn).mockRejectedValue(httpError(403))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="advance-turn-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="run-error"]').text()).toMatch(/only the host/i)
      expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Alice')
    })

    it('shows the server message for a 400 (for example, the adventure is no longer in progress)', async () => {
      vi.mocked(api.advanceAdventureTurn).mockRejectedValue(
        httpError(400, { detail: 'Adventure session is Abandoned; it can no longer be changed.' }),
      )
      const { wrapper } = setup()

      await wrapper.find('[data-testid="advance-turn-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="run-error"]').text()).toContain('can no longer be changed')
    })

    it('is disabled when nobody is in the turn order', () => {
      const { wrapper } = setup(groupRunState({ turnOrder: [], activeParticipantId: null, activeParticipantName: null }))

      expect(wrapper.find('[data-testid="advance-turn-button"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="active-player"]').text()).toBe('Nobody yet')
    })

    it('with a single player, passing starts the next round', () => {
      const run = groupRunState({
        turnOrder: [{ participantId: 'p-alice', participantName: 'Alice', isActive: true }],
      })
      const { wrapper } = setup(run)

      expect(wrapper.find('[data-testid="advance-turn-button"]').text()).toContain('Start the next round')
    })
  })

  describe('host seat', () => {
    it('says the host takes no turn and how to get a seat', () => {
      const { wrapper } = setup()

      expect(wrapper.find('[data-testid="host-seat-hint"]').text()).toMatch(/don't take a turn/i)
      expect(wrapper.find('[data-testid="host-seat-hint"]').text()).toMatch(/join as a player from another browser/i)
    })
  })

  describe('scene control', () => {
    it('advances to the chosen exit', async () => {
      vi.mocked(api.advanceAdventureScene).mockResolvedValue(groupRunState({ currentSceneNumber: 3 }))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="scene-select"]').setValue('scene-4')
      await wrapper.find('[data-testid="advance-scene-button"]').trigger('click')
      await flushPromises()

      expect(api.advanceAdventureScene).toHaveBeenCalledWith('run-1', 'scene-4')
    })

    it('defaults to the first exit', async () => {
      vi.mocked(api.advanceAdventureScene).mockResolvedValue(groupRunState())
      const { wrapper } = setup()

      await wrapper.find('[data-testid="advance-scene-button"]').trigger('click')
      await flushPromises()

      expect(api.advanceAdventureScene).toHaveBeenCalledWith('run-1', 'scene-3')
    })

    it('says so when the scene has no exits', () => {
      const run = groupRunState({ currentScene: makeScene({ exits: [] }) })
      const { wrapper } = setup(run)

      expect(wrapper.find('[data-testid="scene-controls"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="no-exits"]').exists()).toBe(true)
    })
  })

  describe('abandon', () => {
    it('asks first, and does nothing if the host cancels', async () => {
      vi.stubGlobal('confirm', vi.fn(() => false))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="abandon-button"]').trigger('click')
      await flushPromises()

      expect(api.abandonAdventureSession).not.toHaveBeenCalled()
    })

    it('abandons, shows it, and emits abandoned', async () => {
      vi.stubGlobal('confirm', vi.fn(() => true))
      vi.mocked(api.abandonAdventureSession).mockResolvedValue(groupRunState({ status: 'Abandoned' }))
      const { wrapper } = setup()

      await wrapper.find('[data-testid="abandon-button"]').trigger('click')
      await flushPromises()

      expect(api.abandonAdventureSession).toHaveBeenCalledWith('run-1')
      expect(wrapper.find('[data-testid="run-finished"]').text()).toContain('abandoned')
      expect(wrapper.find('[data-testid="advance-turn-button"]').exists()).toBe(false)
      expect(wrapper.emitted('abandoned')).toHaveLength(1)
    })

    it('shows a finished adventure instead of the controls', () => {
      const { wrapper } = setup(groupRunState({ status: 'Completed', outcomeNotes: 'They escaped.' }))

      expect(wrapper.find('[data-testid="run-finished"]').text()).toContain('complete')
      expect(wrapper.find('[data-testid="run-finished"]').text()).toContain('They escaped.')
      expect(wrapper.find('[data-testid="advance-turn-button"]').exists()).toBe(false)
    })
  })

  describe('players who joined late', () => {
    it('lists approved players who are not in the turn order, and adds one to the end', async () => {
      vi.mocked(api.addAdventureParticipant).mockResolvedValue(groupRunState())
      const { wrapper } = setup(groupRunState(), [late('p-dan', 'Dan')])

      expect(wrapper.find('[data-testid="late-players"]').text()).toContain('Dan')
      await wrapper.find('[data-testid="add-late-player"]').trigger('click')
      await flushPromises()

      expect(api.addAdventureParticipant).toHaveBeenCalledWith('run-1', 'p-dan')
    })

    it('shows nothing when everyone is in', () => {
      const { wrapper } = setup()

      expect(wrapper.find('[data-testid="late-players"]').exists()).toBe(false)
    })

    it('shows the server message when the player is already in the adventure', async () => {
      vi.mocked(api.addAdventureParticipant).mockRejectedValue(
        httpError(409, { detail: 'That participant is already in this adventure run.' }),
      )
      const { wrapper } = setup(groupRunState(), [late('p-dan', 'Dan')])

      await wrapper.find('[data-testid="add-late-player"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="run-error"]').text()).toBe('That participant is already in this adventure run.')
    })
  })
})

describe('groupRunStore: hub events', () => {
  function storeWith(run = groupRunState()) {
    setActivePinia(createPinia())
    const store = useGroupRunStore()
    store.setRun(run)
    return store
  }

  it('re-reads the run on an event for this run', async () => {
    const store = storeWith()
    vi.mocked(api.getAdventureSession).mockResolvedValue(groupRunState({ activeParticipantName: 'Bob' }))

    await store.onRunEvent({ sessionId: 'run-1' })

    expect(api.getAdventureSession).toHaveBeenCalledWith('run-1')
    expect(store.run?.activeParticipantName).toBe('Bob')
  })

  it('ignores an event for a different run', async () => {
    const store = storeWith()

    await store.onRunEvent({ sessionId: 'someone-elses-run' })

    expect(api.getAdventureSession).not.toHaveBeenCalled()
  })

  it('on ParticipantJoinedAdventure, shows a note (position plus one) and refreshes', async () => {
    const store = storeWith()
    const withDan = groupRunState()
    withDan.turnOrder = [...withDan.turnOrder, { participantId: 'p-dan', participantName: 'Dan', isActive: false }]
    vi.mocked(api.getAdventureSession).mockResolvedValue(withDan)

    await store.onParticipantJoinedAdventure({
      sessionId: 'run-1',
      participantId: 'p-dan',
      participantName: 'Dan',
      turnPosition: 3, // zero-based: the fourth player
    })

    expect(store.notice).toBe('Dan joined the adventure as player 4.')
    expect(store.run?.turnOrder).toHaveLength(4)
  })

  it('ignores ParticipantJoinedAdventure for a different run', async () => {
    const store = storeWith()

    await store.onParticipantJoinedAdventure({ sessionId: 'other', participantId: 'x', participantName: 'X', turnPosition: 0 })

    expect(store.notice).toBeNull()
    expect(api.getAdventureSession).not.toHaveBeenCalled()
  })

  it('keeps what is on screen when a refresh fails', async () => {
    const store = storeWith()
    vi.mocked(api.getAdventureSession).mockRejectedValue(new Error('offline'))

    await store.refresh()

    expect(store.run?.activeParticipantName).toBe('Alice')
  })

  it('finds the campaigns run on load, and treats no run as no run', async () => {
    setActivePinia(createPinia())
    const store = useGroupRunStore()
    vi.mocked(api.getActiveAdventureSession).mockResolvedValue({ id: 'run-1' } as never)
    vi.mocked(api.getAdventureSession).mockResolvedValue(groupRunState())

    expect((await store.loadForCampaign('campaign-1'))?.isGroup).toBe(true)
    expect(store.isActiveGroupRun).toBe(true)

    vi.mocked(api.getActiveAdventureSession).mockResolvedValue(null)
    expect(await store.loadForCampaign('campaign-1')).toBeNull()
    expect(store.run).toBeNull()
  })

  it('reports a failure to load the run', async () => {
    setActivePinia(createPinia())
    const store = useGroupRunStore()
    vi.mocked(api.getActiveAdventureSession).mockRejectedValue(httpError(500))

    await store.loadForCampaign('campaign-1')

    expect(store.error).toMatch(/could not load the adventure/i)
    expect(store.run).toBeNull()
  })
})

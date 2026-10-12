import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'
import GenerationToast from '../components/GenerationToast.vue'
import AdventureGenerateView from '../AdventureGenerateView.vue'
import { useAdventureGenerationStore } from '../store'
import type { GenerateAdventureRequest } from '../api'

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  closed: null as null | (() => void),
  builds: 0,
  connection: { on: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), start: vi.fn(), invoke: vi.fn(), stop: vi.fn() },
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn().mockReturnValue({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
    build: vi.fn(() => {
      hub.builds++
      return hub.connection
    }),
  }),
  LogLevel: { Warning: 1 },
}))

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return { ...actual, generateAdventure: vi.fn() }
})

import * as api from '../api'

const JOB = 'job-1'
const REQUEST: GenerateAdventureRequest = {
  mode: 'Solo', ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014', partySize: 1, averageLevel: 1,
  tone: 'epic', length: 'one-shot', difficulty: 'medium', combatRatio: 34, explorationRatio: 33,
  roleplayRatio: 33, setting: null, characterIds: null,
}

const emit = (name: string, payload: unknown) => hub.handlers.get(name)!(payload)
const completed = (jobId = JOB) =>
  emit('AdventureGenerationCompleted', { jobId, adventureId: 'adv-1', adventureTitle: 'The Last Light' })

const Stub = defineComponent({ render: () => h('div') })
const Host = defineComponent({ render: () => h('div', [h(RouterView), h(GenerationToast)]) })

/** Mounts the app shell the way App.vue does: the store is initialised once, outside any page. */
async function mountApp() {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/adventures/generate', name: 'adventure-generate', component: AdventureGenerateView },
      { path: '/adventures/:adventureId/review', name: 'adventure-review', component: Stub, props: true },
      { path: '/library', name: 'library', component: Stub },
      { path: '/adventures', name: 'adventures', component: Stub },
      { path: '/campaigns/:campaignId', name: 'campaign-detail', component: Stub, props: true },
      { path: '/campaigns', name: 'campaigns', component: Stub },
    ],
  })
  await router.push('/adventures/generate')
  await router.isReady()
  const store = useAdventureGenerationStore(pinia)
  await store.initialize() // what App.vue does on mount
  const wrapper = mount(Host, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, router, store }
}

const toast = (w: ReturnType<typeof mount>) => w.find('[data-testid="generation-toast"]')

beforeEach(() => {
  vi.clearAllMocks()
  hub.handlers.clear()
  hub.builds = 0
  hub.closed = null
  hub.connection.on.mockImplementation((n: string, cb: (...a: unknown[]) => void) => hub.handlers.set(n, cb))
  hub.connection.onclose.mockImplementation((cb: () => void) => { hub.closed = cb })
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
  vi.mocked(api.generateAdventure).mockResolvedValue({ jobId: JOB, status: 'Queued', message: 'started' })
})

afterEach(() => vi.useRealTimers())

describe('Adventure generation survives navigation', () => {
  it('AdventureGenerationToast_ShowsSuccess_WhenCompletedEventReceivedAfterNavigation', async () => {
    const { wrapper, router, store } = await mountApp()
    await store.start(REQUEST)

    await router.push('/campaigns') // user leaves the Adventure Forge while it generates
    await flushPromises()
    completed()
    await nextTick()

    expect(toast(wrapper).attributes('data-state')).toBe('completed')
    expect(toast(wrapper).text()).toContain('Your adventure is ready!')
    expect(wrapper.find('[data-testid="toast-view-link"]').text()).toContain('View Adventure')
    expect(router.currentRoute.value.path).toBe('/campaigns') // not yanked away
  })

  it('AdventureGenerationToast_ShowsError_WhenFailedEventReceivedAfterNavigation', async () => {
    const { wrapper, router, store } = await mountApp()
    await store.start(REQUEST)

    await router.push('/library')
    await flushPromises()
    emit('AdventureGenerationFailed', { jobId: JOB, error: 'The AI returned invalid JSON.' })
    await nextTick()

    expect(toast(wrapper).text()).toContain('Adventure generation failed')
    expect(wrapper.find('[data-testid="toast-error"]').text()).toContain('invalid JSON')
    expect(wrapper.find('[data-testid="toast-retry"]').exists()).toBe(true)
  })

  it('AdventureGenerationToast_AutoDismisses_OnlyAfterSuccessShown', async () => {
    const { wrapper, router, store } = await mountApp()
    await store.start(REQUEST)
    await router.push('/library')
    await flushPromises()
    vi.useFakeTimers()

    // Generating for far longer than 10s: the in-progress toast must stay.
    emit('AdventureGenerationProgress', { jobId: JOB, stage: 'scenes', percentComplete: 55, message: 'Writing scenes and locations...' })
    vi.advanceTimersByTime(60_000)
    await nextTick()
    expect(toast(wrapper).attributes('data-state')).toBe('generating')

    // The 10s countdown starts when success is shown, not before.
    completed()
    await nextTick()
    vi.advanceTimersByTime(9_900)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(true)
    vi.advanceTimersByTime(200)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(false)
  })

  it('AdventureGenerationStore_ListenersActive_AfterRouteChange', async () => {
    const { router, store } = await mountApp()

    // Listening before any generation starts, and set up exactly once for the app.
    expect(hub.connection.invoke).toHaveBeenCalledWith('JoinAdventureGenerationGroup')
    expect([...hub.handlers.keys()].sort()).toEqual([
      'AdventureGenerationCompleted', 'AdventureGenerationFailed', 'AdventureGenerationProgress',
    ])

    await router.push('/library')
    await router.push('/campaigns')
    await router.push('/adventures/generate')
    await flushPromises()

    expect(hub.connection.stop).not.toHaveBeenCalled() // never torn down by navigation
    expect(hub.builds).toBe(1)
    await store.start(REQUEST)
    expect(hub.builds).toBe(1) // start() reuses the app-level connection
    expect(store.isGenerating).toBe(true)
  })

  it('shows the result of a job the store did not start (page reload / other tab)', async () => {
    const { wrapper, router } = await mountApp() // nothing started in this session
    await router.push('/library')

    completed('job-from-before-reload')
    await nextTick()

    expect(toast(wrapper).text()).toContain('Your adventure is ready!')
  })

  it('ignores late events for a job that was already dismissed', async () => {
    const { wrapper, store } = await mountApp()
    await store.start(REQUEST)
    completed()
    await nextTick()
    store.dismissToast()
    await nextTick()

    emit('AdventureGenerationProgress', { jobId: JOB, stage: 'clues', percentComplete: 92, message: 'late' })
    await nextTick()

    expect(toast(wrapper).exists()).toBe(false)
  })

  it('rebuilds the connection after it closes for good, so later jobs still report', async () => {
    vi.useFakeTimers()
    const { store } = await mountApp()
    expect(hub.builds).toBe(1)

    hub.closed?.() // reconnect attempts exhausted, e.g. the backend restarted
    await vi.advanceTimersByTimeAsync(5_100)
    expect(hub.builds).toBe(2)

    await store.start(REQUEST)
    expect(hub.builds).toBe(2)
  })

  it('keeps retrying when the backend is not up at launch', async () => {
    vi.useFakeTimers()
    hub.connection.start.mockRejectedValueOnce(new Error('offline'))
    const { store } = await mountApp()
    expect(hub.builds).toBe(1)

    await vi.advanceTimersByTimeAsync(5_100)

    expect(hub.builds).toBe(2)
    await store.start(REQUEST)
    expect(store.isGenerating).toBe(true)
  })
})

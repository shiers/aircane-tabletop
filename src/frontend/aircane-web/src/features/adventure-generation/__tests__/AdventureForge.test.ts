import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'
import { RouterView } from 'vue-router'
import AdventureGenerateView from '../AdventureGenerateView.vue'
import GenerationToast from '../components/GenerationToast.vue'
import { useAdventureGenerationStore } from '../store'
import type { GenerateAdventureRequest } from '../api'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  connection: { on: vi.fn(), onreconnected: vi.fn(), start: vi.fn(), invoke: vi.fn(), stop: vi.fn() },
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
  return { ...actual, generateAdventure: vi.fn() }
})

import * as api from '../api'

const JOB = 'job-1'

const REQUEST: GenerateAdventureRequest = {
  mode: 'Solo', ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014', partySize: 1, averageLevel: 3,
  tone: 'epic', length: 'one-shot', difficulty: 'medium', combatRatio: 34, explorationRatio: 33,
  roleplayRatio: 33, setting: null, characterIds: null,
}

function emit(name: string, payload: unknown): void {
  const handler = hub.handlers.get(name)
  if (!handler) throw new Error(`No handler for ${name}`)
  handler(payload)
}

const progress = (percentComplete: number, message: string, stage = 'scenes') =>
  emit('AdventureGenerationProgress', { jobId: JOB, stage, percentComplete, message })
const completed = () =>
  emit('AdventureGenerationCompleted', { jobId: JOB, adventureId: 'adv-1', adventureTitle: 'The Sunken Archive' })

const Stub = defineComponent({ render: () => h('div') })
const Host = defineComponent({ render: () => h('div', [h(RouterView), h(GenerationToast)]) })

async function setup(startAt = '/adventures/generate') {
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/adventures/generate', name: 'adventure-generate', component: AdventureGenerateView },
      { path: '/adventures/:adventureId/review', name: 'adventure-review', component: Stub, props: true },
      { path: '/library', name: 'library', component: Stub },
    ],
  })
  await router.push(startAt)
  await router.isReady()
  const wrapper = mount(Host, {
    global: { plugins: [pinia, router], directives: { 'bg-asset': {} } },
  })
  await flushPromises()
  return { wrapper, router, store: useAdventureGenerationStore(pinia) }
}

const toast = (w: ReturnType<typeof mount>) => w.find('[data-testid="generation-toast"]')

beforeEach(() => {
  vi.clearAllMocks()
  hub.handlers.clear()
  hub.connection.on.mockImplementation((n: string, cb: (...a: unknown[]) => void) => hub.handlers.set(n, cb))
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
  vi.mocked(api.generateAdventure).mockResolvedValue({ jobId: JOB, status: 'Queued', message: 'started' })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('Adventure generation (async, SignalR progress)', () => {
  it('AdventureForge_ShowsProgressToast_AfterSubmit', async () => {
    const { wrapper } = await setup()
    expect(toast(wrapper).exists()).toBe(false)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.generateAdventure).toHaveBeenCalledTimes(1)
    // Listening for progress was set up before the job was queued.
    expect(hub.connection.invoke).toHaveBeenCalledWith('JoinAdventureGenerationGroup')
    expect(toast(wrapper).attributes('data-state')).toBe('generating')
    expect(toast(wrapper).text()).toContain('Generating your adventure')
    expect(wrapper.find('[data-testid="toast-percent"]').text()).toBe('0%')
  })

  it('AdventureForge_UpdatesProgressToast_OnSignalREvent', async () => {
    const { wrapper } = await setup('/library')
    const store = useAdventureGenerationStore()
    await store.start(REQUEST)

    progress(55, 'Writing scenes and locations...')
    await nextTick()

    expect(wrapper.find('[data-testid="toast-message"]').text()).toBe('Writing scenes and locations...')
    expect(wrapper.find('[data-testid="toast-percent"]').text()).toBe('55%')
    expect(wrapper.find('[data-testid="toast-bar"]').attributes('style')).toContain('width: 55%')

    // Events for some other job, and out-of-order older events, are ignored.
    emit('AdventureGenerationProgress', { jobId: 'other', stage: 'npcs', percentComplete: 90, message: 'x' })
    progress(35, 'Building the adventure outline...', 'outline')
    await nextTick()
    expect(wrapper.find('[data-testid="toast-percent"]').text()).toBe('55%')
  })

  it('AdventureForge_ToastNavigatesToReview_OnCompleted', async () => {
    const { wrapper, router, store } = await setup('/library')
    await store.start(REQUEST)

    completed()
    await nextTick()

    expect(toast(wrapper).text()).toContain('Your adventure is ready!')
    expect(toast(wrapper).text()).toContain('The Sunken Archive')
    const link = wrapper.find('[data-testid="toast-view-link"]')
    expect(link.text()).toContain('View Adventure')

    await link.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/adventures/adv-1/review')
    expect(toast(wrapper).exists()).toBe(false)
  })

  it('AdventureForge_ToastShowsError_OnFailed', async () => {
    const { wrapper, store } = await setup('/library')
    await store.start(REQUEST)

    emit('AdventureGenerationFailed', { jobId: JOB, error: "Failed to parse AI response for stage 'scenes'." })
    await nextTick()

    expect(toast(wrapper).text()).toContain('Adventure generation failed')
    expect(wrapper.find('[data-testid="toast-error"]').text()).toContain("stage 'scenes'")

    // Does not auto-dismiss.
    vi.useFakeTimers()
    vi.advanceTimersByTime(60_000)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(true)
    vi.useRealTimers()

    // Try Again queues the same request again.
    await wrapper.find('[data-testid="toast-retry"]').trigger('click')
    await flushPromises()
    expect(api.generateAdventure).toHaveBeenCalledTimes(2)
    expect(vi.mocked(api.generateAdventure).mock.calls[1][0]).toEqual(REQUEST)
    expect(toast(wrapper).attributes('data-state')).toBe('generating')
  })

  it('AdventureForge_FormDisabled_DuringGeneration', async () => {
    const { wrapper, router, store } = await setup()
    await store.start(REQUEST)
    await flushPromises()

    const form = wrapper.find('[data-testid="forge-form"]')
    expect(form.classes()).toContain('opacity-50')
    const fields = form.findAll('input, select, textarea')
    expect(fields.length).toBeGreaterThan(5)
    for (const field of fields) expect(field.attributes('disabled')).toBeDefined()
    const submit = form.find('button[type="submit"]')
    expect(submit.text()).toContain('Generating')
    expect(submit.attributes('disabled')).toBeDefined()

    // Navigating away is free, and coming back shows the progress inline.
    progress(70, 'Creating characters and NPCs...', 'npcs')
    await router.push('/library')
    await router.push('/adventures/generate')
    await flushPromises()
    expect(wrapper.find('[data-testid="forge-progress-message"]').text()).toBe('Creating characters and NPCs...')
    expect(wrapper.find('[data-testid="forge-form"] input').attributes('disabled')).toBeDefined()
  })

  it('the form is enabled again once generation has finished', async () => {
    const { wrapper, store } = await setup()
    await store.start(REQUEST)
    completed() // on the forge page: auto-navigates away, so check the store gate instead
    await flushPromises()

    expect(store.isGenerating).toBe(false)
  })

  it('AdventureForge_AutoNavigates_WhenStillOnPage_OnCompleted', async () => {
    const { router, store } = await setup('/adventures/generate')
    await store.start(REQUEST)

    completed()
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/adventures/adv-1/review')
  })

  it('AdventureForge_DoesNotAutoNavigate_WhenElsewhere_OnCompleted', async () => {
    const { wrapper, router, store } = await setup('/library')
    await store.start(REQUEST)

    completed()
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/library')
    expect(toast(wrapper).exists()).toBe(true)
  })

  it('AdventureForge_ToastAutoDismisses_After10Seconds', async () => {
    const { wrapper, store } = await setup('/library')
    await store.start(REQUEST)
    vi.useFakeTimers()

    completed()
    await nextTick()
    expect(toast(wrapper).exists()).toBe(true)

    vi.advanceTimersByTime(9_900)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(true)

    vi.advanceTimersByTime(200)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(false)
  })

  it('AdventureForge_ToastStays_WhenHovered', async () => {
    const { wrapper, store } = await setup('/library')
    await store.start(REQUEST)
    vi.useFakeTimers()

    completed()
    await nextTick()
    await toast(wrapper).trigger('mouseenter')

    vi.advanceTimersByTime(30_000)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(true)

    // Leaving restarts the countdown.
    await toast(wrapper).trigger('mouseleave')
    vi.advanceTimersByTime(10_100)
    await nextTick()
    expect(toast(wrapper).exists()).toBe(false)
  })

  it('a failed request leaves the form usable and shows the server message', async () => {
    vi.mocked(api.generateAdventure).mockRejectedValue({
      response: { data: { errors: { PartySize: ['Party size must be between 1 and 10.'] } } },
    })
    const { wrapper, store } = await setup()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('Party size must be between 1 and 10.')
    expect(store.isGenerating).toBe(false)
    expect(toast(wrapper).exists()).toBe(false)
  })

  it('refuses to start when live progress cannot be connected (no orphan job)', async () => {
    hub.connection.start.mockRejectedValue(new Error('offline'))
    const { wrapper } = await setup()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.generateAdventure).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('Could not connect for live progress')
  })
})

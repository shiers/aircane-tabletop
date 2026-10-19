import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory, RouterView } from 'vue-router'
import { defineComponent, h, nextTick } from 'vue'
import AdventuresView from '../AdventuresView.vue'
import GenerationToast from '../components/GenerationToast.vue'
import { useAdventureGenerationStore } from '../store'
import { useCampaignStore } from '@/features/campaigns/store'
import { useAdventureSessionStore } from '@/features/adventures/stores/adventureSessionStore'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'
import type { CampaignAdventure, GenerateAdventureRequest } from '../api'

const hub = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => void>(),
  connection: { on: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), start: vi.fn(), invoke: vi.fn(), stop: vi.fn() },
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

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  listUnassignedAdventures: vi.fn(),
  assignAdventureToCampaign: vi.fn(),
  approveAdventure: vi.fn(),
  deleteAdventure: vi.fn(),
  generateAdventure: vi.fn(),
}))

import * as api from '../api'

// Resume reads the run first (a group run goes to the host screen, a solo run to the solo screen).
vi.mock('@/features/adventures/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventures/api')>()),
  getAdventureSession: vi.fn(),
  getActiveAdventureSession: vi.fn(),
}))

import * as runApi from '@/features/adventures/api'
import { makeState, groupRunState } from '@/features/adventures/__tests__/fixtures'

const campaign = (id: string, name: string): CampaignDto => ({
  id, name, gameSystem: 'D&D 5e', ruleset: '2014', gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
  createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
})
const CAMPAIGNS = [campaign('c1', 'Friday Night'), campaign('c2', 'Solo Nights')]

const adventure = (over: Partial<CampaignAdventure> = {}): CampaignAdventure => ({
  id: 'adv-1', title: 'The Last Light of the First Dawn', status: 'Approved', gameSystem: 'D&D 5e 2014',
  ruleset: 'D&D 5e 2014', mode: 'Solo', length: 'one-shot', averageLevel: 1, createdAt: '2026-10-12T02:35:00Z', ...over,
})
const DRAFT = () => adventure({ id: 'adv-2', title: 'The Last Light of Dawnspire', status: 'Draft' })

const REQUEST: GenerateAdventureRequest = {
  mode: 'Solo', ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014', partySize: 1, averageLevel: 1,
  tone: 'epic', length: 'one-shot', difficulty: 'medium', combatRatio: 34, explorationRatio: 33,
  roleplayRatio: 33, setting: null, characterIds: null,
}

const Stub = defineComponent({ render: () => h('div') })
const Host = defineComponent({ render: () => h('div', [h(RouterView), h(GenerationToast)]) })

async function mountPage(unassigned: CampaignAdventure[]) {
  vi.mocked(api.listUnassignedAdventures).mockResolvedValue(unassigned)
  const pinia = createPinia()
  setActivePinia(pinia)
  const campaigns = useCampaignStore(pinia)
  vi.spyOn(campaigns, 'fetchCampaigns').mockImplementation(async () => {
    campaigns.campaigns = CAMPAIGNS
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/adventures', name: 'adventures', component: AdventuresView },
      { path: '/adventures/generate', name: 'adventure-generate', component: Stub },
      { path: '/adventures/:adventureId/review', name: 'adventure-review', component: Stub, props: true },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: Stub },
      { path: '/sessions/:sessionId/host', name: 'host-session', component: Stub, props: true },
      { path: '/campaigns', name: 'campaigns', component: Stub },
      { path: '/campaigns/:campaignId', name: 'campaign-detail', component: Stub, props: true },
    ],
  })
  await router.push('/adventures')
  await router.isReady()
  const wrapper = mount(Host, { global: { plugins: [pinia, router] } })
  await flushPromises()
  return { wrapper, router, pinia }
}

const cards = (w: ReturnType<typeof mount>) => w.findAll('[data-testid="adventure-card"]')
const buttons = (card: ReturnType<typeof cards>[number]) =>
  card.findAll('button').map((b) => b.text())

beforeEach(() => {
  vi.clearAllMocks()
  hub.handlers.clear()
  hub.connection.on.mockImplementation((n: string, cb: (...a: unknown[]) => void) => hub.handlers.set(n, cb))
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  vi.mocked(api.generateAdventure).mockResolvedValue({ jobId: 'job-1', status: 'Queued', message: '' })
})
afterEach(() => vi.unstubAllGlobals())

describe('Adventure Forge: unassigned adventures', () => {
  it('AdventureForge_ShowsUnassignedAdventures_OnLoad', async () => {
    const { wrapper } = await mountPage([adventure(), DRAFT()])

    expect(api.listUnassignedAdventures).toHaveBeenCalledTimes(1)
    expect(wrapper.find('h1').text()).toBe('Adventure Forge')
    expect(wrapper.find('#unassigned-heading').text()).toBe('Unassigned Adventures')
    expect(cards(wrapper)).toHaveLength(2)
    expect(cards(wrapper)[0].find('[data-testid="adventure-title"]').text()).toBe('✦ The Last Light of the First Dawn')
    expect(cards(wrapper)[0].find('[data-testid="adventure-meta"]').text()).toBe('D&D 5e 2014 · Solo · One-shot · Level 1')
  })

  it('AdventureForge_ShowsDraftAndApprovedAdventures', async () => {
    const { wrapper } = await mountPage([adventure(), DRAFT()])
    const [approved, draft] = cards(wrapper)

    expect(approved.find('[data-testid="adventure-status"]').text()).toBe('Approved')
    expect(buttons(approved)).toEqual(['Assign to Campaign', 'Review', '▶ Play Solo', 'Delete'])
    expect(draft.find('[data-testid="adventure-status"]').text()).toBe('Draft')
    expect(buttons(draft)).toEqual(['Assign to Campaign', 'Approve', 'Review', 'Delete'])
  })

  it('AdventureForge_ShowsEmptyState_WhenNoUnassigned', async () => {
    const { wrapper } = await mountPage([])

    expect(wrapper.find('[data-testid="unassigned-empty"]').text()).toBe('No unassigned adventures.')
    expect(cards(wrapper)).toHaveLength(0)
  })

  it('AdventureForge_AssignToCampaign_RemovesFromUnassignedList', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockResolvedValue(adventure())
    const { wrapper } = await mountPage([adventure(), DRAFT()])

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()
    const options = cards(wrapper)[0].findAll('[data-testid="assign-select"] option').map((o) => o.text())
    expect(options).toEqual(['Choose a campaign…', 'Friday Night', 'Solo Nights'])

    await cards(wrapper)[0].find('[data-testid="assign-select"]').setValue('c2')
    await flushPromises()

    expect(api.assignAdventureToCampaign).toHaveBeenCalledWith('adv-1', 'c2')
    expect(cards(wrapper)).toHaveLength(1)
    expect(wrapper.text()).not.toContain('The Last Light of the First Dawn')
    expect(wrapper.text()).toContain('The Last Light of Dawnspire')
  })

  it('a failed assignment keeps the adventure in the list and says why', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockRejectedValue(
      Object.assign(new Error('x'), { response: { data: { detail: 'Campaign not found' } } }),
    )
    const { wrapper } = await mountPage([adventure()])

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()
    await cards(wrapper)[0].find('[data-testid="assign-select"]').setValue('c1')
    await flushPromises()

    expect(cards(wrapper)).toHaveLength(1)
    expect(wrapper.find('[data-testid="adventures-error"]').text()).toContain('Campaign not found')
  })

  it('AdventureForge_ApproveButton_UpdatesStatusInPlace', async () => {
    vi.mocked(api.approveAdventure).mockResolvedValue(undefined)
    const { wrapper } = await mountPage([DRAFT()])

    await wrapper.find('[data-testid="approve-button"]').trigger('click')
    await flushPromises()

    expect(api.approveAdventure).toHaveBeenCalledWith('adv-2')
    expect(api.listUnassignedAdventures).toHaveBeenCalledTimes(1) // no reload
    const card = cards(wrapper)[0]
    expect(card.find('[data-testid="adventure-status"]').text()).toBe('Approved')
    expect(buttons(card)).toEqual(['Assign to Campaign', 'Review', '▶ Play Solo', 'Delete'])
  })

  it('AdventureForge_DeleteButton_RemovesAdventureAfterConfirmation', async () => {
    vi.mocked(api.deleteAdventure).mockResolvedValue(undefined)
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountPage([adventure(), DRAFT()])

    await wrapper.findAll('[data-testid="delete-button"]')[0].trigger('click')
    await flushPromises()
    expect(api.deleteAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(2)

    confirmFn.mockReturnValue(true)
    await wrapper.findAll('[data-testid="delete-button"]')[0].trigger('click')
    await flushPromises()

    expect(api.deleteAdventure).toHaveBeenCalledWith('adv-1')
    expect(cards(wrapper)).toHaveLength(1)
  })

  it('AdventureForge_PlaySolo_NavigatesToSoloSession', async () => {
    const { wrapper, router, pinia } = await mountPage([adventure()])
    const start = vi.spyOn(useAdventureSessionStore(pinia), 'startSession').mockResolvedValue('run-9')

    // An unassigned adventure has no campaign, so Play Solo asks which one to play in.
    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()
    await wrapper.find('[data-testid="campaign-select"]').setValue('c1')
    await wrapper.find('[data-testid="start-button"]').trigger('click')
    await flushPromises()

    expect(start).toHaveBeenCalledWith('adv-1', 'c1')
    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-9')
  })

  it('AdventureForge_ShowsResume_ForAnUnassignedAdventureWithARunInProgress', async () => {
    const running = adventure({
      activeSession: { sessionId: 'run-3', currentSceneNumber: 2, totalScenes: 5, status: 'InProgress' },
    })
    const { wrapper, router, pinia } = await mountPage([running, adventure({ id: 'adv-9', title: 'Idle' })])
    const start = vi.spyOn(useAdventureSessionStore(pinia), 'startSession')
    const [first, second] = cards(wrapper)

    expect(first.find('[data-testid="adventure-progress"]').text()).toBe('Scene 2 of 5 — In Progress')
    expect(buttons(first)).toEqual(['Assign to Campaign', 'Review', 'Resume', 'Delete'])
    expect(first.find('[data-testid="play-solo-button"]').exists()).toBe(false)
    expect(buttons(second)).toContain('▶ Play Solo')   // an idle one is unchanged

    vi.mocked(runApi.getAdventureSession).mockResolvedValue(makeState({ id: 'run-3' }))
    await first.find('[data-testid="resume-adventure-button"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-3')
    expect(start).not.toHaveBeenCalled()
  })

  it('AdventureForge_Resume_OpensAGroupRunOnTheHostScreen', async () => {
    const running = adventure({
      mode: 'Group',
      activeSession: { sessionId: 'run-4', currentSceneNumber: 3, totalScenes: 6, status: 'InProgress' },
    })
    const { wrapper, router } = await mountPage([running])
    vi.mocked(runApi.getAdventureSession).mockResolvedValue(groupRunState({ id: 'run-4', sessionId: 'play-77' }))

    await cards(wrapper)[0].find('[data-testid="resume-adventure-button"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/sessions/play-77/host')
  })

  it('AdventureForge_Resume_FallsBackToTheSoloScreenWhenTheRunCannotBeRead', async () => {
    const running = adventure({
      activeSession: { sessionId: 'run-5', currentSceneNumber: 1, totalScenes: 4, status: 'InProgress' },
    })
    const { wrapper, router } = await mountPage([running])
    vi.mocked(runApi.getAdventureSession).mockRejectedValue(new Error('offline'))

    await cards(wrapper)[0].find('[data-testid="resume-adventure-button"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-5')
  })

  // ── Button branching by adventure mode ────────────────────────────────────

  it('AdventureForge_GroupAdventure_OffersStartGroupSession_NotPlaySolo', async () => {
    const { wrapper } = await mountPage([adventure({ mode: 'Group' })])
    const [card] = cards(wrapper)

    expect(buttons(card)).toEqual(['Assign to Campaign', 'Review', '👥 Start Group Session', 'Delete'])
    expect(card.find('[data-testid="start-group-button"]').exists()).toBe(true)
    expect(card.find('[data-testid="play-solo-button"]').exists()).toBe(false)
  })

  it('AdventureForge_SoloAdventure_OffersPlaySolo_NotStartGroupSession', async () => {
    const { wrapper } = await mountPage([adventure({ mode: 'Solo' })])
    const [card] = cards(wrapper)

    expect(buttons(card)).toContain('▶ Play Solo')
    expect(card.find('[data-testid="play-solo-button"]').exists()).toBe(true)
    expect(card.find('[data-testid="start-group-button"]').exists()).toBe(false)
  })

  it('AdventureForge_AdventureWithNoMode_IsOfferedAsSolo', async () => {
    const { wrapper } = await mountPage([adventure({ mode: null })])

    expect(cards(wrapper)[0].find('[data-testid="play-solo-button"]').exists()).toBe(true)
    expect(cards(wrapper)[0].find('[data-testid="start-group-button"]').exists()).toBe(false)
  })

  it('AdventureForge_ModeMatchingIgnoresCase', async () => {
    const { wrapper } = await mountPage([adventure({ mode: 'group' })])

    expect(cards(wrapper)[0].find('[data-testid="start-group-button"]').exists()).toBe(true)
  })

  it('AdventureForge_GroupAdventure_IsNotPlayableUntilApproved', async () => {
    const { wrapper } = await mountPage([adventure({ mode: 'Group', status: 'Draft' })])
    const [card] = cards(wrapper)

    expect(card.find('[data-testid="start-group-button"]').exists()).toBe(false)
    expect(card.find('[data-testid="play-solo-button"]').exists()).toBe(false)
  })

  it('AdventureForge_GroupAdventure_WithARunInProgress_ShowsResumeInsteadOfStart', async () => {
    const running = adventure({
      mode: 'Group',
      activeSession: { sessionId: 'run-4', currentSceneNumber: 3, totalScenes: 6, status: 'InProgress' },
    })
    const { wrapper } = await mountPage([running])

    expect(buttons(cards(wrapper)[0])).toEqual(['Assign to Campaign', 'Review', 'Resume', 'Delete'])
  })

  // ── The assign picker must tell "no campaigns" from "could not load them" ──

  it('AdventureForge_AssignPicker_SaysWhenCampaignsCouldNotBeLoaded_NotThatThereAreNone', async () => {
    const { wrapper, pinia } = await mountPage([adventure()])
    vi.spyOn(useCampaignStore(pinia), 'fetchCampaigns').mockRejectedValue(new Error('boom'))
    useCampaignStore(pinia).campaigns = []

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="assign-load-failed"]').text()).toContain('Could not load your campaigns')
    expect(wrapper.find('[data-testid="assign-no-campaigns"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('You have no campaigns yet')
  })

  it('AdventureForge_AssignPicker_RetryRecoversFromALoadFailure', async () => {
    const { wrapper, pinia } = await mountPage([adventure()])
    const campaigns = useCampaignStore(pinia)
    campaigns.campaigns = []
    vi.spyOn(campaigns, 'fetchCampaigns')
      .mockRejectedValueOnce(new Error('boom'))
      .mockImplementationOnce(async () => {
        campaigns.campaigns = CAMPAIGNS
      })

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()
    await wrapper.find('[data-testid="assign-retry"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="assign-load-failed"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="assign-select"]').exists()).toBe(true)
  })

  it('AdventureForge_AssignPicker_StillSaysNoCampaignsWhenThereReallyAreNone', async () => {
    const { wrapper, pinia } = await mountPage([adventure()])
    const campaigns = useCampaignStore(pinia)
    campaigns.campaigns = []
    vi.spyOn(campaigns, 'fetchCampaigns').mockImplementation(async () => {
      campaigns.campaigns = []
    })

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="assign-no-campaigns"]').text()).toContain('You have no campaigns yet')
    expect(wrapper.find('[data-testid="assign-load-failed"]').exists()).toBe(false)
  })

  it('Review opens the adventure review screen', async () => {
    const { wrapper, router } = await mountPage([adventure()])

    await wrapper.find('[data-testid="review-button"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/adventures/adv-1/review')
  })

  it('AdventureForge_GenerateButton_NavigatesToGenerateRoute', async () => {
    const { wrapper, router } = await mountPage([adventure()])

    await wrapper.find('[data-testid="generate-new"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/adventures/generate')
    expect(router.currentRoute.value.query.campaignId).toBeUndefined() // no campaign pre-selected
  })

  it('a newly generated (unassigned) adventure appears in the list when generation completes', async () => {
    const { pinia } = await mountPage([])
    const store = useAdventureGenerationStore(pinia)
    await store.start(REQUEST)
    vi.mocked(api.listUnassignedAdventures).mockResolvedValue([adventure({ title: 'Fresh' })])

    hub.handlers.get('AdventureGenerationCompleted')!({ jobId: 'job-1', adventureId: 'adv-new', adventureTitle: 'Fresh' })
    await flushPromises()

    expect(api.listUnassignedAdventures).toHaveBeenCalledTimes(2)
  })
})

describe('Generation toast link', () => {
  it('AdventureForge_Toast_LinksToCampaignPage_WhenGeneratedForACampaign', async () => {
    const { wrapper, router, pinia } = await mountPage([])
    await router.push('/campaigns')
    await useAdventureGenerationStore(pinia).start(REQUEST, 'c1')

    hub.handlers.get('AdventureGenerationCompleted')!({ jobId: 'job-1', adventureId: 'adv-new', adventureTitle: 'Fresh' })
    await nextTick()
    await wrapper.find('[data-testid="toast-view-link"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/campaigns/c1')
  })
})

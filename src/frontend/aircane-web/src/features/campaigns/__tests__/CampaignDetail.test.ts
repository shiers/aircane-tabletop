import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'
import CampaignDetail from '../CampaignDetail.vue'
import AdventureGenerateView from '@/features/adventure-generation/AdventureGenerateView.vue'
import AdventureReviewView from '@/features/adventure-generation/AdventureReviewView.vue'
import { useAdventureSessionStore } from '@/features/adventures/stores/adventureSessionStore'
import { AiRole, AiAuthority, type CampaignDto } from '../api'
import type { CampaignAdventure } from '@/features/adventure-generation/api'

const hub = vi.hoisted(() => ({
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

vi.mock('../api', async (importOriginal) => ({ ...(await importOriginal<typeof import('../api')>()), getCampaign: vi.fn() }))
vi.mock('@/features/adventure-generation/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventure-generation/api')>()),
  listCampaignAdventures: vi.fn(),
  approveAdventure: vi.fn(),
  deleteAdventure: vi.fn(),
  generateAdventure: vi.fn(),
  getAdventureDraft: vi.fn(),
}))
vi.mock('@/features/adventures/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventures/api')>()),
  getActiveAdventureSession: vi.fn(),
}))

import * as campaignApi from '../api'
import * as genApi from '@/features/adventure-generation/api'
import * as soloApi from '@/features/adventures/api'

const CAMPAIGN_ID = 'campaign-1'

const campaign: CampaignDto = {
  id: CAMPAIGN_ID, name: 'Friday Night', gameSystem: 'D&D 5e', ruleset: '2014', gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
  createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
}

const adventure = (over: Partial<CampaignAdventure> = {}): CampaignAdventure => ({
  id: 'adv-1', title: 'The Last Light of the First Dawn', status: 'Approved', gameSystem: 'D&D 5e 2014',
  ruleset: 'D&D 5e 2014', mode: 'Solo', length: 'one-shot', averageLevel: 1, createdAt: '2026-10-12T02:35:00Z', ...over,
})

const Stub = defineComponent({ render: () => h('div') })

async function mountDetail(adventures: CampaignAdventure[]) {
  vi.mocked(campaignApi.getCampaign).mockResolvedValue(campaign)
  vi.mocked(genApi.listCampaignAdventures).mockResolvedValue(adventures)
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/campaigns', name: 'campaigns', component: Stub },
      { path: `/campaigns/:campaignId`, name: 'campaign-detail', component: CampaignDetail, props: true },
      { path: '/adventures', name: 'adventures', component: Stub },
      { path: '/adventures/generate', name: 'adventure-generate', component: AdventureGenerateView },
      { path: '/adventures/:adventureId/review', name: 'adventure-review', component: AdventureReviewView, props: true },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: Stub },
    ],
  })
  await router.push(`/campaigns/${CAMPAIGN_ID}`)
  await router.isReady()
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, router, pinia }
}

const cards = (w: ReturnType<typeof mount>) => w.findAll('[data-testid="adventure-card"]')

beforeEach(() => {
  vi.clearAllMocks()
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
})
afterEach(() => vi.unstubAllGlobals())

describe('CampaignDetail', () => {
  it('CampaignDetail_ShowsAdventuresSection', async () => {
    const { wrapper } = await mountDetail([
      adventure(),
      adventure({ id: 'adv-2', title: 'The Last Light of Dawnspire', status: 'Draft' }),
    ])

    expect(wrapper.find('[data-testid="campaign-name"]').text()).toBe('Friday Night')
    expect(wrapper.find('#campaign-adventures-heading').text()).toBe('Adventures')
    expect(cards(wrapper)).toHaveLength(2)
    const [approved, draft] = cards(wrapper)
    expect(approved.text()).toContain('✦ The Last Light of the First Dawn')
    expect(approved.find('[data-testid="adventure-status"]').text()).toBe('Approved')
    expect(approved.find('[data-testid="adventure-meta"]').text()).toBe('D&D 5e 2014 · Solo · One-shot · Level 1')

    // Actions depend on status.
    const labels = (c: typeof approved) => c.findAll('button').map((b) => b.text())
    expect(labels(draft)).toEqual(['Review', 'Approve', 'Delete'])
    expect(labels(approved)).toEqual(['Review', 'Play Solo', 'Delete'])
  })

  it('CampaignDetail_ShowsEmptyState_WhenNoAdventures', async () => {
    const { wrapper } = await mountDetail([])

    expect(wrapper.find('[data-testid="adventures-empty"]').text()).toContain('No adventures yet.')
    expect(cards(wrapper)).toHaveLength(0)
    // The one Generate button is the empty state's, not duplicated in the header.
    expect(wrapper.findAll('[data-testid="generate-adventure"]')).toHaveLength(1)
  })

  it('CampaignDetail_ApproveButton_ChangesStatusToApproved', async () => {
    vi.mocked(genApi.approveAdventure).mockResolvedValue(undefined)
    const { wrapper } = await mountDetail([adventure({ status: 'Draft' })])

    await wrapper.find('[data-testid="approve-button"]').trigger('click')
    await flushPromises()

    expect(genApi.approveAdventure).toHaveBeenCalledWith('adv-1')
    expect(genApi.listCampaignAdventures).toHaveBeenCalledTimes(1) // refreshed in place, not reloaded
    const card = cards(wrapper)[0]
    expect(card.find('[data-testid="adventure-status"]').text()).toBe('Approved')
    expect(card.findAll('button').map((b) => b.text())).toEqual(['Review', 'Play Solo', 'Delete'])
  })

  it('CampaignDetail_DeleteButton_RemovesAdventureAfterConfirmation', async () => {
    vi.mocked(genApi.deleteAdventure).mockResolvedValue(undefined)
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([adventure(), adventure({ id: 'adv-2', title: 'Second' })])

    // Declined: nothing happens.
    await wrapper.findAll('[data-testid="delete-button"]')[0].trigger('click')
    await flushPromises()
    expect(genApi.deleteAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(2)

    // Confirmed: deleted and removed from the list.
    confirmFn.mockReturnValue(true)
    await wrapper.findAll('[data-testid="delete-button"]')[0].trigger('click')
    await flushPromises()
    expect(confirmFn).toHaveBeenLastCalledWith(expect.stringContaining('The Last Light of the First Dawn'))
    expect(genApi.deleteAdventure).toHaveBeenCalledWith('adv-1')
    expect(cards(wrapper)).toHaveLength(1)
    expect(wrapper.text()).toContain('Second')
  })

  it('CampaignDetail_PlaySolo_NavigatesToSoloSession', async () => {
    const { wrapper, router, pinia } = await mountDetail([adventure()])
    const start = vi.spyOn(useAdventureSessionStore(pinia), 'startSession').mockResolvedValue('run-9')

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()

    expect(start).toHaveBeenCalledWith('adv-1', CAMPAIGN_ID) // starts the run in THIS campaign
    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-9')
  })

  it('Play Solo offers to resume when the campaign already has an adventure in progress', async () => {
    const { wrapper, router, pinia } = await mountDetail([adventure()])
    const store = useAdventureSessionStore(pinia)
    vi.spyOn(store, 'startSession').mockImplementation(async () => {
      store.error = 'This campaign already has an adventure in progress.'
      throw Object.assign(new Error('conflict'), { response: { status: 409 } })
    })
    vi.mocked(soloApi.getActiveAdventureSession).mockResolvedValue({ id: 'run-existing' } as never)

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="adventures-error"]').text()).toContain('already has an adventure in progress')

    await wrapper.find('[data-testid="resume-button"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-existing')
  })

  const running = (over: Partial<CampaignAdventure> = {}) =>
    adventure({ activeSession: { sessionId: 'run-7', currentSceneNumber: 1, totalScenes: 5, status: 'InProgress' }, ...over })

  it('CampaignDetail_ShowsResumeButton_WhenSessionInProgress', async () => {
    const { wrapper, router } = await mountDetail([running()])
    const card = cards(wrapper)[0]

    expect(card.findAll('button').map((b) => b.text())).toEqual(['Review', 'Resume', 'Delete'])
    expect(card.find('[data-testid="play-solo-button"]').exists()).toBe(false)

    // Resume opens the existing run: it does not start a new one.
    const start = vi.spyOn(useAdventureSessionStore(), 'startSession')
    await card.find('[data-testid="resume-adventure-button"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-7')
    expect(start).not.toHaveBeenCalled()
  })

  it('CampaignDetail_ShowsSceneProgress_WhenSessionInProgress', async () => {
    const { wrapper } = await mountDetail([running()])

    expect(cards(wrapper)[0].find('[data-testid="adventure-progress"]').text()).toBe('Scene 1 of 5 — In Progress')
  })

  it('CampaignDetail_ShowsPlaySolo_WhenNoActiveSession', async () => {
    const { wrapper } = await mountDetail([adventure(), adventure({ id: 'adv-2', title: 'Other', activeSession: null })])

    for (const card of cards(wrapper)) {
      expect(card.find('[data-testid="play-solo-button"]').exists()).toBe(true)
      expect(card.find('[data-testid="resume-adventure-button"]').exists()).toBe(false)
      expect(card.find('[data-testid="adventure-progress"]').exists()).toBe(false)
    }
  })

  it('CampaignDetail_GenerateAdventureButton_NavigatesToForgeWithCampaignId', async () => {
    const { wrapper, router } = await mountDetail([adventure()])

    await wrapper.find('[data-testid="generate-adventure"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/adventures/generate')
    expect(router.currentRoute.value.query.campaignId).toBe(CAMPAIGN_ID)
  })

  it('Review navigates to the adventure review screen', async () => {
    const { wrapper, router } = await mountDetail([adventure()])
    vi.mocked(genApi.getAdventureDraft).mockResolvedValue({ id: 'adv-1', title: 't', status: 'Approved' } as never)

    await wrapper.find('[data-testid="review-button"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/adventures/adv-1/review')
  })
})

describe('Adventure Forge and review screen with a campaign', () => {
  it('the Forge generates into the campaign it was opened from', async () => {
    vi.mocked(genApi.generateAdventure).mockResolvedValue({ jobId: 'job-1', status: 'Queued', message: '' })
    const { wrapper, router } = await mountDetail([])
    await wrapper.find('[data-testid="generate-adventure"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query.campaignId).toBe(CAMPAIGN_ID)

    expect(wrapper.find('[data-testid="forge-campaign"]').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(genApi.generateAdventure).toHaveBeenCalledTimes(1)
    expect(vi.mocked(genApi.generateAdventure).mock.calls[0][1]).toBe(CAMPAIGN_ID)
  })

  it('the Forge generates with no campaign when opened directly', async () => {
    vi.mocked(genApi.generateAdventure).mockResolvedValue({ jobId: 'job-1', status: 'Queued', message: '' })
    const { wrapper, router } = await mountDetail([])
    await router.push('/adventures/generate')
    await flushPromises()

    expect(wrapper.find('[data-testid="forge-campaign"]').exists()).toBe(false)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(vi.mocked(genApi.generateAdventure).mock.calls[0][1]).toBeNull()
  })

  it('Back on the review screen returns to the campaign page when the adventure has a campaign', async () => {
    const { wrapper, router } = await mountDetail([])
    vi.mocked(genApi.getAdventureDraft).mockResolvedValue({
      id: 'adv-1', title: 'T', status: 'Draft', campaignId: CAMPAIGN_ID, createdAt: '', updatedAt: '',
    })
    await router.push('/adventures/adv-1/review')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('Back'))!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe(`/campaigns/${CAMPAIGN_ID}`)
  })

  it('Back on the review screen returns to the Adventure Forge list for an adventure without a campaign', async () => {
    const { wrapper, router } = await mountDetail([])
    vi.mocked(genApi.getAdventureDraft).mockResolvedValue({
      id: 'adv-1', title: 'T', status: 'Draft', campaignId: null, createdAt: '', updatedAt: '',
    })
    await router.push('/adventures/adv-1/review')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('Back'))!.trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/adventures')
  })
})

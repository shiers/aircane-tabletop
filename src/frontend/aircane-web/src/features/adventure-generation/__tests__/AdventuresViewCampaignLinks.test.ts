import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'
import AdventuresView from '../AdventuresView.vue'
import { useCampaignStore } from '@/features/campaigns/store'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'
import type { CampaignAdventure } from '../api'

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn().mockReturnValue({
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
    build: vi.fn(() => ({ on: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(), start: vi.fn(), invoke: vi.fn(), stop: vi.fn() })),
  }),
  LogLevel: { Warning: 1 },
}))

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  listAllAdventures: vi.fn(),
  assignAdventureToCampaign: vi.fn(),
  unassignAdventure: vi.fn(),
  approveAdventure: vi.fn(),
  deleteAdventure: vi.fn(),
  getAdventureDeleteImpact: vi.fn(),
}))

import * as api from '../api'

const campaign = (id: string, name: string): CampaignDto => ({
  id, name, gameSystem: 'D&D 5e', ruleset: '2014', gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
  createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
})
const CAMPAIGNS = [campaign('c1', 'Friday Night'), campaign('c2', 'Tuesday Group')]

const adventure = (over: Partial<CampaignAdventure> = {}): CampaignAdventure => ({
  id: 'adv-1', title: 'Loose End', status: 'Approved', gameSystem: 'D&D 5e 2014', ruleset: 'D&D 5e 2014',
  mode: 'Solo', length: 'one-shot', averageLevel: 1, createdAt: '2026-10-12T02:35:00Z', campaignId: null, campaignName: null, ...over,
})
const IN_C1 = () => adventure({ id: 'adv-2', title: 'In Friday', campaignId: 'c1', campaignName: 'Friday Night' })

const conflict = (data: Record<string, unknown>) =>
  Object.assign(new Error('Request failed with status code 409'), { response: { status: 409, data } })

const Stub = defineComponent({ render: () => h('div') })

async function mountPage(list: CampaignAdventure[]) {
  vi.mocked(api.listAllAdventures).mockResolvedValue(list)
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
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountPage>>['wrapper']
const cards = (w: Wrapper) => w.findAll('[data-testid="adventure-card"]')
const buttons = (c: ReturnType<typeof cards>[number]) => c.findAll('button, a').map((b) => b.text())

beforeEach(() => vi.clearAllMocks())
afterEach(() => vi.unstubAllGlobals())

describe('Adventure Forge: every adventure, with its campaign', () => {
  it('lists all adventures, the ones in no campaign first, each assigned one labelled with its campaign', async () => {
    const { wrapper } = await mountPage([IN_C1(), adventure()])

    expect(wrapper.find('#adventures-heading').text()).toBe('Your Adventures')
    expect(cards(wrapper).map((c) => c.find('[data-testid="adventure-title"]').text())).toEqual(['✦ Loose End', '✦ In Friday'])
    expect(cards(wrapper)[0].find('[data-testid="adventure-campaign"]').exists()).toBe(false)
    expect(cards(wrapper)[1].find('[data-testid="adventure-campaign"]').text()).toBe('In campaign: Friday Night')
  })

  it('gives an assigned adventure Remove from campaign, and an unassigned one Assign to Campaign', async () => {
    const { wrapper } = await mountPage([adventure(), IN_C1()])

    expect(buttons(cards(wrapper)[0])).toEqual(['Assign to Campaign', 'Review', '▶ Play Solo', '🗑 Delete adventure'])
    expect(buttons(cards(wrapper)[1])).toEqual([
      'Move to Campaign', 'Review', 'Remove from campaign', 'Open campaign', '🗑 Delete adventure',
    ])
  })

  it('Remove from campaign names the campaign, then unassigns and keeps the adventure in the list', async () => {
    vi.mocked(api.unassignAdventure).mockResolvedValue(adventure({ id: 'adv-2', title: 'In Friday' }))
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountPage([IN_C1()])

    await cards(wrapper)[0].find('[data-testid="remove-from-campaign-button"]').trigger('click')
    await flushPromises()
    expect(api.unassignAdventure).not.toHaveBeenCalled()

    confirmFn.mockReturnValue(true)
    await cards(wrapper)[0].find('[data-testid="remove-from-campaign-button"]').trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('In Friday')
    expect(asked).toContain('Friday Night')
    expect(asked).toContain('stays in the Adventure Forge')
    expect(api.unassignAdventure).toHaveBeenCalledWith('adv-2')
    expect(api.deleteAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(1)
    expect(cards(wrapper)[0].find('[data-testid="adventure-campaign"]').exists()).toBe(false)
    expect(buttons(cards(wrapper)[0])).toContain('Assign to Campaign')
  })

  it('the Assign picker handles the other-campaign 409 by naming both campaigns, then sends confirmMove', async () => {
    vi.mocked(api.assignAdventureToCampaign)
      .mockRejectedValueOnce(conflict({ code: 'adventure-in-other-campaign', currentCampaignName: 'Friday Night' }))
      .mockResolvedValueOnce({ ...IN_C1(), campaignId: 'c2', campaignName: 'Tuesday Group' })
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountPage([IN_C1()])

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()
    // The campaign it is in already is not offered again.
    expect(cards(wrapper)[0].findAll('[data-testid="assign-select"] option').map((o) => o.text()))
      .toEqual(['Choose a campaign…', 'Tuesday Group'])
    await cards(wrapper)[0].find('[data-testid="assign-select"]').setValue('c2')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('In Friday')
    expect(asked).toContain('Friday Night')
    expect(asked).toContain('Tuesday Group')
    expect(api.assignAdventureToCampaign).toHaveBeenLastCalledWith('adv-2', 'c2', { confirmMove: true })
    expect(cards(wrapper)[0].find('[data-testid="adventure-campaign"]').text()).toBe('In campaign: Tuesday Group')
  })

  it('the Assign picker handles the game-system 409, and declining attaches nothing', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockRejectedValue(
      conflict({ code: 'game-system-mismatch', adventureGameSystem: 'D&D 5e 2014', campaignGameSystem: 'Pathfinder 2e' }),
    )
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountPage([adventure()])

    await cards(wrapper)[0].find('[data-testid="assign-button"]').trigger('click')
    await flushPromises()
    await cards(wrapper)[0].find('[data-testid="assign-select"]').setValue('c1')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('D&D 5e 2014')
    expect(asked).toContain('Pathfinder 2e')
    expect(api.assignAdventureToCampaign).toHaveBeenCalledTimes(1)
    expect(cards(wrapper)[0].find('[data-testid="adventure-campaign"]').exists()).toBe(false)
  })

  it('holds the adventure while a run is in progress: detach, move and delete are disabled with the reason', async () => {
    const running = IN_C1()
    running.activeSession = { sessionId: 'run-1', currentSceneNumber: 1, totalScenes: 4, status: 'InProgress' }
    const { wrapper } = await mountPage([running])

    const card = cards(wrapper)[0]
    for (const id of ['assign-button', 'remove-from-campaign-button', 'delete-button']) {
      expect(card.find(`[data-testid="${id}"]`).attributes('disabled')).toBeDefined()
    }
    expect(card.find('[data-testid="run-holds-note"]').text()).toContain('run of this adventure is in progress')
  })

  it('Delete adventure fetches the impact and names what goes', async () => {
    vi.mocked(api.getAdventureDeleteImpact).mockResolvedValue({
      id: 'adv-1', title: 'Loose End', totalRuns: 1, notStartedRuns: 0, inProgressRuns: 0,
      completedRuns: 1, abandonedRuns: 0, transcriptEntries: 1, indexedChunks: 0,
    })
    vi.mocked(api.deleteAdventure).mockResolvedValue(undefined)
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountPage([adventure()])

    await cards(wrapper)[0].find('[data-testid="delete-button"]').trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('"Loose End"')
    expect(asked).toContain('1 run (1 completed)')
    expect(asked).toContain('1 transcript entry')
    expect(asked).toContain('cannot be undone')
    expect(api.deleteAdventure).toHaveBeenCalledWith('adv-1')
  })

  it('a failed load shows the server message and a retry, never "No adventures yet"', async () => {
    const failed = await mountPageWithFirstLoadFailing()

    expect(failed.find('[data-testid="adventures-error"]').text()).toContain('The database is unavailable.')
    expect(failed.find('[data-testid="unassigned-empty"]').exists()).toBe(false)
    expect(cards(failed)).toHaveLength(0)

    await failed.find('[data-testid="adventures-retry"]').trigger('click')
    await flushPromises()
    expect(failed.find('[data-testid="adventures-error"]').exists()).toBe(false)
    expect(cards(failed)).toHaveLength(1)
  })
})

async function mountPageWithFirstLoadFailing(): Promise<Wrapper> {
  // mountPage sets a resolved value for every call; queue the failure after it so the first load rejects.
  vi.mocked(api.listAllAdventures).mockReset()
  vi.mocked(api.listAllAdventures)
    .mockRejectedValueOnce(Object.assign(new Error('boom'), { response: { data: { detail: 'The database is unavailable.' } } }))
    .mockResolvedValue([adventure()])
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
      { path: '/campaigns', name: 'campaigns', component: Stub },
      { path: '/campaigns/:campaignId', name: 'campaign-detail', component: Stub, props: true },
    ],
  })
  await router.push('/adventures')
  await router.isReady()
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return wrapper
}

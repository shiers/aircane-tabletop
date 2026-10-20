import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'
import CampaignDetail from '../CampaignDetail.vue'
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
  listAllAdventures: vi.fn(),
  assignAdventureToCampaign: vi.fn(),
  unassignAdventure: vi.fn(),
  deleteAdventure: vi.fn(),
  getAdventureDeleteImpact: vi.fn(),
}))

import * as campaignApi from '../api'
import * as genApi from '@/features/adventure-generation/api'

const CAMPAIGN_ID = 'campaign-1'

const campaign: CampaignDto = {
  id: CAMPAIGN_ID, name: 'Friday Night', gameSystem: 'D&D 5e 2014', ruleset: '2014', gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
  createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
}

const adventure = (over: Partial<CampaignAdventure> = {}): CampaignAdventure => ({
  id: 'adv-1', title: 'The Sunken Archive', status: 'Approved', gameSystem: 'D&D 5e 2014', ruleset: 'D&D 5e 2014',
  mode: 'Solo', length: 'one-shot', averageLevel: 3, createdAt: '2026-10-12T02:35:00Z', ...over,
})

/** The server's 409: a problem body with a code. */
const conflict = (data: Record<string, unknown>) =>
  Object.assign(new Error('Request failed with status code 409'), { response: { status: 409, data } })

const Stub = defineComponent({ render: () => h('div') })

async function mountDetail(inCampaign: CampaignAdventure[], everything: CampaignAdventure[] = [], firstLoadError?: object) {
  vi.mocked(campaignApi.getCampaign).mockResolvedValue(campaign)
  vi.mocked(genApi.listCampaignAdventures).mockResolvedValue(inCampaign)
  if (firstLoadError) {
    vi.mocked(genApi.listCampaignAdventures).mockRejectedValueOnce(Object.assign(new Error('boom'), firstLoadError))
  }
  vi.mocked(genApi.listAllAdventures).mockResolvedValue(everything)
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/campaigns', name: 'campaigns', component: Stub },
      { path: '/campaigns/:campaignId', name: 'campaign-detail', component: CampaignDetail, props: true },
      { path: '/adventures', name: 'adventures', component: Stub },
      { path: '/adventures/generate', name: 'adventure-generate', component: Stub },
      { path: '/adventures/:adventureId/review', name: 'adventure-review', component: Stub, props: true },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: Stub },
      { path: '/sessions/:sessionId/host', name: 'host-session', component: Stub, props: true },
    ],
  })
  await router.push(`/campaigns/${CAMPAIGN_ID}`)
  await router.isReady()
  const wrapper = mount({ template: '<RouterView />' }, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, router }
}

type Wrapper = Awaited<ReturnType<typeof mountDetail>>['wrapper']
const cards = (w: Wrapper) => w.findAll('[data-testid="adventure-card"]')
const options = (w: Wrapper) => w.findAll('[data-testid="attach-option"]')
const openPicker = async (w: Wrapper) => {
  await w.find('[data-testid="attach-adventure"]').trigger('click')
  await flushPromises()
}

const IMPACT = {
  id: 'adv-1', title: 'The Sunken Archive', totalRuns: 3, notStartedRuns: 0, inProgressRuns: 0,
  completedRuns: 2, abandonedRuns: 1, transcriptEntries: 41, indexedChunks: 12,
}

beforeEach(() => {
  vi.clearAllMocks()
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
})
afterEach(() => vi.unstubAllGlobals())

describe('Campaign page: the two ways to let an adventure go', () => {
  it('offers Remove from campaign and a separate, visually distinct Delete adventure', async () => {
    const { wrapper } = await mountDetail([adventure()])

    const card = cards(wrapper)[0]
    const remove = card.find('[data-testid="remove-from-campaign-button"]')
    const del = card.find('[data-testid="delete-button"]')
    expect(remove.text()).toBe('Remove from campaign')
    expect(del.text()).toContain('Delete adventure')
    expect(remove.element).not.toBe(del.element)
    expect(del.classes()).toContain('adventure-action--danger')
    expect(remove.classes()).not.toContain('adventure-action--danger')
  })

  it('Remove from campaign asks lightly, says the adventure stays in the Forge, then detaches only', async () => {
    vi.mocked(genApi.unassignAdventure).mockResolvedValue(adventure())
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([adventure(), adventure({ id: 'adv-2', title: 'Second' })])

    await cards(wrapper)[0].find('[data-testid="remove-from-campaign-button"]').trigger('click')
    await flushPromises()
    expect(genApi.unassignAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(2)

    confirmFn.mockReturnValue(true)
    await cards(wrapper)[0].find('[data-testid="remove-from-campaign-button"]').trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('The Sunken Archive')
    expect(asked).toContain('stays in the Adventure Forge')
    expect(genApi.unassignAdventure).toHaveBeenCalledWith('adv-1')
    expect(genApi.deleteAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(1)
  })

  it('Delete adventure names the adventure and exactly what goes, and says it cannot be undone', async () => {
    vi.mocked(genApi.getAdventureDeleteImpact).mockResolvedValue(IMPACT)
    vi.mocked(genApi.deleteAdventure).mockResolvedValue(undefined)
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([adventure()])

    await cards(wrapper)[0].find('[data-testid="delete-button"]').trigger('click')
    await flushPromises()

    expect(genApi.getAdventureDeleteImpact).toHaveBeenCalledWith('adv-1')
    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('"The Sunken Archive"')
    expect(asked).toContain('3 runs (2 completed, 1 abandoned)')
    expect(asked).toContain('41 transcript entries')
    expect(asked).toContain('indexed content')
    expect(asked).toContain('12 pieces')
    expect(asked).toContain('cannot be undone')
    expect(genApi.deleteAdventure).toHaveBeenCalledWith('adv-1')
    expect(genApi.unassignAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(0)
  })

  it('a declined delete deletes nothing', async () => {
    vi.mocked(genApi.getAdventureDeleteImpact).mockResolvedValue(IMPACT)
    vi.stubGlobal('confirm', vi.fn(() => false))
    const { wrapper } = await mountDetail([adventure()])

    await cards(wrapper)[0].find('[data-testid="delete-button"]').trigger('click')
    await flushPromises()

    expect(genApi.deleteAdventure).not.toHaveBeenCalled()
    expect(cards(wrapper)).toHaveLength(1)
  })

  it('both buttons are disabled, with the reason shown, while a run is in progress', async () => {
    const { wrapper } = await mountDetail([
      adventure({ activeSession: { sessionId: 'run-1', currentSceneNumber: 2, totalScenes: 5, status: 'InProgress' } }),
    ])

    const card = cards(wrapper)[0]
    expect(card.find('[data-testid="remove-from-campaign-button"]').attributes('disabled')).toBeDefined()
    expect(card.find('[data-testid="delete-button"]').attributes('disabled')).toBeDefined()
    expect(card.find('[data-testid="run-holds-note"]').text()).toContain('run of this adventure is in progress')
  })

  it("shows the server's message when a run it knows about stops the removal or the delete", async () => {
    const detail = '"The Sunken Archive" has a run in progress, so it cannot be taken out of its campaign. Finish or abandon the run first.'
    vi.mocked(genApi.unassignAdventure).mockRejectedValue(
      conflict({ code: 'adventure-run-in-progress', detail }),
    )
    vi.stubGlobal('confirm', vi.fn(() => true))
    const { wrapper } = await mountDetail([adventure()])

    await cards(wrapper)[0].find('[data-testid="remove-from-campaign-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="adventures-error"]').text()).toContain(detail)
    expect(cards(wrapper)).toHaveLength(1)
  })

  it('does not even ask to delete when the impact says a run is in progress', async () => {
    vi.mocked(genApi.getAdventureDeleteImpact).mockResolvedValue({ ...IMPACT, inProgressRuns: 1 })
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([adventure()])

    await cards(wrapper)[0].find('[data-testid="delete-button"]').trigger('click')
    await flushPromises()

    expect(confirmFn).not.toHaveBeenCalled()
    expect(genApi.deleteAdventure).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="adventures-error"]').text()).toContain('run in progress')
  })

  it('a failed load says so and offers a retry, never "No adventures yet"', async () => {
    const { wrapper } = await mountDetail([adventure()], [], {
      response: { data: { detail: 'The database is unavailable.' } },
    })

    expect(wrapper.find('[data-testid="adventures-error"]').text()).toContain('The database is unavailable.')
    expect(wrapper.find('[data-testid="adventures-empty"]').exists()).toBe(false)
    expect(cards(wrapper)).toHaveLength(0)

    await wrapper.find('[data-testid="adventures-retry"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="adventures-error"]').exists()).toBe(false)
    expect(cards(wrapper)).toHaveLength(1)
  })
})

describe('Campaign page: attaching an adventure', () => {
  const LOOSE = adventure({ id: 'loose', title: 'Loose End', status: 'Draft', mode: 'Group', averageLevel: 5, campaignId: null })
  const ELSEWHERE = adventure({ id: 'else', title: 'Over There', campaignId: 'other', campaignName: 'Tuesday Group' })
  const HERE = adventure({ id: 'here', title: 'Already Here', campaignId: CAMPAIGN_ID, campaignName: 'Friday Night' })

  it('has an Attach adventure button next to Generate Adventure', async () => {
    const { wrapper } = await mountDetail([adventure()])

    const header = wrapper.find('#campaign-adventures-heading').element.parentElement!
    expect(header.querySelector('[data-testid="attach-adventure"]')).not.toBeNull()
    expect(header.querySelector('[data-testid="generate-adventure"]')).not.toBeNull()
  })

  it('lists unassigned adventures with title, mode, level, status and game system', async () => {
    const { wrapper } = await mountDetail([], [ELSEWHERE, LOOSE, HERE])

    await openPicker(wrapper)

    expect(options(wrapper)).toHaveLength(1)
    const option = options(wrapper)[0]
    expect(option.find('[data-testid="attach-option-title"]').text()).toBe('Loose End')
    expect(option.find('[data-testid="attach-option-status"]').text()).toBe('Draft') // drafts can be attached, and say so
    expect(option.find('[data-testid="attach-option-meta"]').text()).toBe('Group · Level 5 · D&D 5e 2014')
    expect(option.find('[data-testid="attach-option-campaign"]').exists()).toBe(false)
  })

  it('a toggle also shows adventures that are in other campaigns, labelled with where they are, after the unassigned ones', async () => {
    const { wrapper } = await mountDetail([], [ELSEWHERE, LOOSE, HERE])
    await openPicker(wrapper)

    await wrapper.find('[data-testid="attach-show-assigned"]').setValue(true)

    expect(options(wrapper).map((o) => o.find('[data-testid="attach-option-title"]').text())).toEqual(['Loose End', 'Over There'])
    expect(options(wrapper)[1].find('[data-testid="attach-option-campaign"]').text()).toBe('currently in Tuesday Group')
    // Never offers what is in this campaign already.
    expect(wrapper.text()).not.toContain('Already Here')
  })

  it('attaches the picked adventure and shows it on the page', async () => {
    vi.mocked(genApi.assignAdventureToCampaign).mockResolvedValue({ ...LOOSE, campaignId: CAMPAIGN_ID })
    const { wrapper } = await mountDetail([], [LOOSE])
    await openPicker(wrapper)

    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()

    expect(genApi.assignAdventureToCampaign).toHaveBeenCalledWith('loose', CAMPAIGN_ID, {})
    expect(wrapper.find('[data-testid="attach-picker"]').exists()).toBe(false)
    expect(cards(wrapper)).toHaveLength(1)
    expect(cards(wrapper)[0].find('[data-testid="adventure-title"]').text()).toContain('Loose End')
  })

  it('a game system mismatch names both sides, and only a yes sends it again with confirmMismatch', async () => {
    const mismatch = conflict({
      code: 'game-system-mismatch', detail: 'x', adventureGameSystem: 'Pathfinder 2e', campaignGameSystem: 'D&D 5e 2014',
    })
    vi.mocked(genApi.assignAdventureToCampaign)
      .mockRejectedValueOnce(mismatch) // the first click, declined
      .mockRejectedValueOnce(mismatch) // the second click, agreed...
      .mockResolvedValueOnce({ ...LOOSE, campaignId: CAMPAIGN_ID }) // ...and sent again with the flag
    const confirmFn = vi.fn(() => false)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([], [LOOSE])
    await openPicker(wrapper)

    // Declined: nothing is attached.
    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()
    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('Loose End')
    expect(asked).toContain('Pathfinder 2e')
    expect(asked).toContain('D&D 5e 2014')
    expect(asked).toContain('Friday Night')
    expect(genApi.assignAdventureToCampaign).toHaveBeenCalledTimes(1)
    expect(cards(wrapper)).toHaveLength(0)

    // Agreed: sent again with the flag.
    confirmFn.mockReturnValue(true)
    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()
    expect(genApi.assignAdventureToCampaign).toHaveBeenLastCalledWith('loose', CAMPAIGN_ID, { confirmMismatch: true })
  })

  it('an adventure in another campaign names both campaigns, and a yes sends it again with confirmMove', async () => {
    vi.mocked(genApi.assignAdventureToCampaign)
      .mockRejectedValueOnce(conflict({
        code: 'adventure-in-other-campaign', detail: 'x', currentCampaignId: 'other', currentCampaignName: 'Tuesday Group',
      }))
      .mockResolvedValueOnce({ ...ELSEWHERE, campaignId: CAMPAIGN_ID })
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([], [ELSEWHERE])
    await openPicker(wrapper)
    await wrapper.find('[data-testid="attach-show-assigned"]').setValue(true)

    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()

    const asked = confirmFn.mock.calls.at(-1)?.[0] as unknown as string
    expect(asked).toContain('Over There')
    expect(asked).toContain('Tuesday Group')
    expect(asked).toContain('Friday Night')
    expect(genApi.assignAdventureToCampaign).toHaveBeenLastCalledWith('else', CAMPAIGN_ID, { confirmMove: true })
    expect(cards(wrapper)).toHaveLength(1)
  })

  it('asks for both agreements when both are needed, then goes through', async () => {
    vi.mocked(genApi.assignAdventureToCampaign)
      .mockRejectedValueOnce(conflict({ code: 'adventure-in-other-campaign', currentCampaignName: 'Tuesday Group' }))
      .mockRejectedValueOnce(conflict({ code: 'game-system-mismatch', adventureGameSystem: 'A', campaignGameSystem: 'B' }))
      .mockResolvedValueOnce({ ...ELSEWHERE, campaignId: CAMPAIGN_ID })
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([], [ELSEWHERE])
    await openPicker(wrapper)
    await wrapper.find('[data-testid="attach-show-assigned"]').setValue(true)

    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()

    expect(confirmFn).toHaveBeenCalledTimes(2)
    expect(genApi.assignAdventureToCampaign).toHaveBeenLastCalledWith('else', CAMPAIGN_ID, {
      confirmMove: true, confirmMismatch: true,
    })
  })

  it("a run in progress is not offered a confirmation: the server's message is shown", async () => {
    const detail = '"Over There" has a run in progress, so it cannot be moved to another campaign. Finish or abandon the run first.'
    vi.mocked(genApi.assignAdventureToCampaign).mockRejectedValue(conflict({ code: 'adventure-run-in-progress', detail }))
    const confirmFn = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmFn)
    const { wrapper } = await mountDetail([], [ELSEWHERE])
    await openPicker(wrapper)
    await wrapper.find('[data-testid="attach-show-assigned"]').setValue(true)

    await options(wrapper)[0].find('[data-testid="attach-option-button"]').trigger('click')
    await flushPromises()

    expect(confirmFn).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="attach-error"]').text()).toContain(detail)
    expect(cards(wrapper)).toHaveLength(0)
  })

  it('says there is nothing to attach, with a link to the Forge', async () => {
    const { wrapper } = await mountDetail([], [HERE])
    await openPicker(wrapper)

    expect(wrapper.find('[data-testid="attach-empty"]').text()).toContain('nothing to attach')
    expect(wrapper.find('[data-testid="attach-forge-link"]').attributes('href')).toBe('/adventures')
  })

  it('points at the toggle when only adventures in other campaigns exist', async () => {
    const { wrapper } = await mountDetail([], [ELSEWHERE])
    await openPicker(wrapper)

    expect(wrapper.find('[data-testid="attach-empty"]').text()).toContain('1 adventure is in other campaigns')
  })

  it('a failed load shows the server\'s message and a retry, never "nothing to attach"', async () => {
    const { wrapper } = await mountDetail([], [LOOSE])
    vi.mocked(genApi.listAllAdventures).mockRejectedValueOnce(
      Object.assign(new Error('boom'), { response: { data: { detail: 'The database is unavailable.' } } }),
    )

    await openPicker(wrapper)
    expect(wrapper.find('[data-testid="attach-load-failed"]').text()).toContain('The database is unavailable.')
    expect(wrapper.find('[data-testid="attach-empty"]').exists()).toBe(false)
    expect(options(wrapper)).toHaveLength(0)

    await wrapper.find('[data-testid="attach-retry"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="attach-load-failed"]').exists()).toBe(false)
    expect(options(wrapper)).toHaveLength(1)
  })

  it('keeps the picker usable at phone width: rows stack and long text wraps', async () => {
    const { wrapper } = await mountDetail([], [LOOSE])
    await openPicker(wrapper)

    expect(options(wrapper)[0].classes()).toEqual(expect.arrayContaining(['flex-col']))
    expect(options(wrapper)[0].find('[data-testid="attach-option-title"]').classes()).toContain('break-words')
    expect(wrapper.find('#campaign-adventures-heading').element.parentElement!.querySelector('.flex-wrap')).not.toBeNull()
  })
})

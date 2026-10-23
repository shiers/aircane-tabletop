import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import AdventureGenerateView from '../AdventureGenerateView.vue'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'
import type { GameSystemDefinitionSummary } from '@/features/game-systems/types'

// ---------------------------------------------------------------------------
// Mocks: the hub, the three APIs the form talks to
// ---------------------------------------------------------------------------

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

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  generateAdventure: vi.fn(),
}))
vi.mock('@/features/game-systems/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/game-systems/api')>()),
  listGameSystems: vi.fn(),
}))
vi.mock('@/features/campaigns/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/campaigns/api')>()),
  listCampaigns: vi.fn(),
}))

import * as api from '../api'
import * as gameSystemsApi from '@/features/game-systems/api'
import * as campaignsApi from '@/features/campaigns/api'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const DND_NAME = 'Dungeons & Dragons 5th Edition (2014)'
const NOT_LISTED = '__not-listed__'
const NOTE = 'No built-in rules for this system. Aircane will use your uploaded rulebooks and general knowledge.'

function definition(over: Partial<GameSystemDefinitionSummary> = {}): GameSystemDefinitionSummary {
  return {
    id: 'def-x', identifier: 'x', name: 'X', version: '1.0.0', publisher: null, genre: null, description: null,
    license: 'test', isBuiltIn: false, isActive: true, createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
    ...over,
  }
}

const DND = definition({ id: 'def-dnd', identifier: 'dnd-5e-2014', name: DND_NAME, isBuiltIn: true })
const PF2E = definition({ id: 'def-pf2e', identifier: 'pathfinder-2e-remaster', name: 'Pathfinder 2e (Remaster)', isBuiltIn: true })
const SHADOWRUN = definition({ id: 'def-sr', identifier: 'shadowrun-6e', name: 'Shadowrun Sixth World', version: '6.0' })

function campaign(over: Partial<CampaignDto> = {}): CampaignDto {
  return {
    id: 'c1', name: 'Friday Night', gameSystem: 'D&D 5e', ruleset: '2014', gameSystemDefinitionId: null,
    aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
    createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
    ...over,
  }
}

interface Setup {
  definitions?: GameSystemDefinitionSummary[] | 'pending' | 'fail'
  campaigns?: CampaignDto[] | 'pending'
}

async function mountForge(path = '/adventures/generate', { definitions = [DND, PF2E, SHADOWRUN], campaigns = [] }: Setup = {}) {
  if (definitions === 'pending') vi.mocked(gameSystemsApi.listGameSystems).mockReturnValue(new Promise(() => {}))
  else if (definitions === 'fail') vi.mocked(gameSystemsApi.listGameSystems).mockRejectedValue(new Error('Network Error'))
  else vi.mocked(gameSystemsApi.listGameSystems).mockResolvedValue(definitions)

  let resolveCampaigns: (value: CampaignDto[]) => void = () => {}
  if (campaigns === 'pending') {
    vi.mocked(campaignsApi.listCampaigns).mockReturnValue(new Promise((resolve) => { resolveCampaigns = resolve }))
  } else {
    vi.mocked(campaignsApi.listCampaigns).mockResolvedValue(campaigns)
  }

  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/adventures/generate', name: 'adventure-generate', component: AdventureGenerateView }],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(AdventureGenerateView, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, resolveCampaigns }
}

type Wrapper = Awaited<ReturnType<typeof mountForge>>['wrapper']

const selector = (w: Wrapper) => w.find<HTMLSelectElement>('#game-system-selector')
const rulesetBox = (w: Wrapper) => w.find<HTMLInputElement>('#ruleset')
const systemBox = (w: Wrapper) => w.find<HTMLInputElement>('#game-system')
const campaignSystem = (w: Wrapper) => w.find('[data-testid="forge-campaign-system"]')

async function submit(w: Wrapper): Promise<void> {
  await w.find('form').trigger('submit')
  await flushPromises()
}

function sent() {
  expect(api.generateAdventure).toHaveBeenCalledTimes(1)
  const [request, campaignId] = vi.mocked(api.generateAdventure).mock.calls[0]
  return { request, campaignId }
}

beforeEach(() => {
  vi.clearAllMocks()
  hub.connection.start.mockResolvedValue(undefined)
  hub.connection.invoke.mockResolvedValue(undefined)
  hub.connection.stop.mockResolvedValue(undefined)
  vi.mocked(api.generateAdventure).mockResolvedValue({ jobId: 'job-1', status: 'Queued', message: 'started' })
})

// ---------------------------------------------------------------------------

describe('Adventure Forge: no campaign', () => {
  it('preselects the D&D 5e 2014 definition, and sends its id with its name as the ruleset and the game system', async () => {
    const { wrapper } = await mountForge()

    expect(selector(wrapper).element.value).toBe('def-dnd')
    expect(rulesetBox(wrapper).exists()).toBe(false)
    expect(systemBox(wrapper).exists()).toBe(false)
    expect(campaignSystem(wrapper).exists()).toBe(false)

    await submit(wrapper)

    const { request, campaignId } = sent()
    expect(request).toMatchObject({ gameSystemDefinitionId: 'def-dnd', ruleset: DND_NAME, gameSystem: DND_NAME, mode: 'Solo', averageLevel: 1 })
    expect(campaignId).toBeNull()
  })

  it('submitted before the definitions have loaded, sends what it always did: the D&D 5e 2014 strings and no id', async () => {
    const { wrapper } = await mountForge('/adventures/generate', { definitions: 'pending' })

    await submit(wrapper)

    expect(sent().request).toMatchObject({ gameSystemDefinitionId: null, ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014' })
  })

  it('with no D&D definition on offer, starts on "Not listed" with the D&D 5e 2014 strings filled in', async () => {
    const { wrapper } = await mountForge('/adventures/generate', { definitions: [SHADOWRUN] })

    expect(selector(wrapper).element.value).toBe(NOT_LISTED)
    expect(rulesetBox(wrapper).element.value).toBe('D&D 5e 2014')
    expect(systemBox(wrapper).element.value).toBe('D&D 5e 2014')

    await submit(wrapper)

    expect(sent().request).toMatchObject({ gameSystemDefinitionId: null, ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014' })
  })

  it('when the definitions cannot be loaded, still works: "Not listed" with the D&D 5e 2014 strings', async () => {
    const { wrapper } = await mountForge('/adventures/generate', { definitions: 'fail' })

    expect(selector(wrapper).element.value).toBe(NOT_LISTED)

    await submit(wrapper)

    expect(sent().request).toMatchObject({ gameSystemDefinitionId: null, ruleset: 'D&D 5e 2014', gameSystem: 'D&D 5e 2014' })
  })

  it('sends the definition that was picked', async () => {
    const { wrapper } = await mountForge()

    await selector(wrapper).setValue('def-pf2e')
    await submit(wrapper)

    expect(sent().request).toMatchObject({
      gameSystemDefinitionId: 'def-pf2e', ruleset: 'Pathfinder 2e (Remaster)', gameSystem: 'Pathfinder 2e (Remaster)',
    })
  })

  it('leaves the Level field as it was', async () => {
    const { wrapper } = await mountForge()

    expect(wrapper.find<HTMLInputElement>('#average-level').element.value).toBe('1')
    expect(wrapper.find('label[for="average-level"]').text()).toBe('Character Level')
    await wrapper.find('#average-level').setValue(7)
    await submit(wrapper)

    expect(sent().request.averageLevel).toBe(7)
  })
})

describe('Adventure Forge: "Not listed"', () => {
  async function chooseNotListed() {
    const { wrapper } = await mountForge()
    await selector(wrapper).setValue(NOT_LISTED)
    return wrapper
  }

  it('reveals the two boxes, empty and required, with a note about what Aircane will use', async () => {
    const wrapper = await chooseNotListed()

    expect(wrapper.find('[data-testid="forge-unlisted-note"]').text()).toBe(NOTE)
    for (const box of [rulesetBox(wrapper), systemBox(wrapper)]) {
      expect(box.element.value).toBe('')
      expect(box.attributes('required')).toBeDefined()
      expect(box.attributes('aria-required')).toBe('true')
    }
  })

  it('refuses to generate while either box is blank, and says so', async () => {
    const wrapper = await chooseNotListed()

    await submit(wrapper)
    expect(wrapper.find('[role="alert"]').text()).toContain('Enter the ruleset and the game system')

    await rulesetBox(wrapper).setValue('6th Edition')
    await systemBox(wrapper).setValue('   ')
    await submit(wrapper)

    expect(api.generateAdventure).not.toHaveBeenCalled()
  })

  it('sends the two strings, trimmed, and no definition id', async () => {
    const wrapper = await chooseNotListed()

    await rulesetBox(wrapper).setValue('  6th Edition ')
    await systemBox(wrapper).setValue(' Shadowrun')
    await submit(wrapper)

    expect(sent().request).toMatchObject({ gameSystemDefinitionId: null, ruleset: '6th Edition', gameSystem: 'Shadowrun' })
  })

  it('hides the boxes again, and sends the definition, when one is picked after all', async () => {
    const wrapper = await chooseNotListed()

    await selector(wrapper).setValue('def-sr')

    expect(wrapper.find('[data-testid="forge-unlisted"]').exists()).toBe(false)
    await submit(wrapper)
    expect(sent().request).toMatchObject({ gameSystemDefinitionId: 'def-sr', gameSystem: 'Shadowrun Sixth World' })
  })
})

describe('Adventure Forge: opened from a campaign', () => {
  const PATH = '/adventures/generate?campaignId=c1'

  it('preselects the definition the campaign is bound to, says it is the campaign\'s, and sends it with the campaign', async () => {
    const { wrapper } = await mountForge(PATH, { campaigns: [campaign({ gameSystemDefinitionId: 'def-sr', gameSystem: 'ignored', ruleset: 'ignored' })] })

    expect(campaignSystem(wrapper).text()).toContain("Using the campaign's game system")
    expect(wrapper.find('[data-testid="forge-campaign-system-name"]').text()).toBe('Shadowrun Sixth World')
    expect(selector(wrapper).exists()).toBe(false)
    expect(wrapper.find('[data-testid="forge-campaign"]').text()).toContain('Friday Night')

    await submit(wrapper)

    const { request, campaignId } = sent()
    expect(request).toMatchObject({ gameSystemDefinitionId: 'def-sr', ruleset: 'Shadowrun Sixth World', gameSystem: 'Shadowrun Sixth World' })
    expect(campaignId).toBe('c1')
  })

  it('finds the definition of a campaign made from the game system selector, which carries its name', async () => {
    const { wrapper } = await mountForge(PATH, { campaigns: [campaign({ gameSystem: ' shadowrun  SIXTH world ', ruleset: '6.0' })] })

    expect(wrapper.find('[data-testid="forge-campaign-system-name"]').text()).toBe('Shadowrun Sixth World')

    await submit(wrapper)

    expect(sent().request).toMatchObject({ gameSystemDefinitionId: 'def-sr', ruleset: 'Shadowrun Sixth World' })
  })

  it('falls back to "Not listed" with the campaign\'s own strings when it matches no definition', async () => {
    const { wrapper } = await mountForge(PATH, { campaigns: [campaign({ gameSystem: 'Call of Cthulhu 7e', ruleset: 'Keeper rules' })] })

    expect(wrapper.find('[data-testid="forge-campaign-system-name"]').text()).toBe('Call of Cthulhu 7e · Keeper rules')

    await wrapper.find('[data-testid="forge-change-system"]').trigger('click')
    expect(selector(wrapper).element.value).toBe(NOT_LISTED)
    expect(rulesetBox(wrapper).element.value).toBe('Keeper rules')
    expect(systemBox(wrapper).element.value).toBe('Call of Cthulhu 7e')

    await submit(wrapper)

    const { request, campaignId } = sent()
    expect(request).toMatchObject({ gameSystemDefinitionId: null, ruleset: 'Keeper rules', gameSystem: 'Call of Cthulhu 7e' })
    expect(campaignId).toBe('c1')
  })

  it('uses the campaign\'s strings when the definition it is bound to is no longer active', async () => {
    const { wrapper } = await mountForge(PATH, {
      campaigns: [campaign({ gameSystemDefinitionId: 'retired', gameSystem: 'Shadowrun Sixth World', ruleset: '6.0' })],
    })

    await submit(wrapper)

    // Not matched by name either: a bound campaign whose definition is gone is not guessed at.
    expect(sent().request).toMatchObject({ gameSystemDefinitionId: null, ruleset: '6.0', gameSystem: 'Shadowrun Sixth World' })
  })

  it('lets the user change it, warns that the adventure may not match the campaign, and can go back', async () => {
    const { wrapper } = await mountForge(PATH, { campaigns: [campaign({ gameSystemDefinitionId: 'def-sr' })] })
    expect(wrapper.find('[data-testid="forge-system-changed"]').exists()).toBe(false)

    await wrapper.find('[data-testid="forge-change-system"]').trigger('click')

    expect(campaignSystem(wrapper).exists()).toBe(false)
    expect(selector(wrapper).element.value).toBe('def-sr')
    expect(wrapper.find('[data-testid="forge-system-changed"]').text()).toContain("may not match the campaign's game system")

    await selector(wrapper).setValue('def-pf2e')
    await submit(wrapper)
    expect(sent().request).toMatchObject({ gameSystemDefinitionId: 'def-pf2e' })
    expect(sent().campaignId).toBe('c1')
  })

  it('goes back to the campaign\'s game system with "Use the campaign\'s game system"', async () => {
    const { wrapper } = await mountForge(PATH, { campaigns: [campaign({ gameSystemDefinitionId: 'def-sr' })] })
    await wrapper.find('[data-testid="forge-change-system"]').trigger('click')
    await selector(wrapper).setValue('def-pf2e')

    await wrapper.find('[data-testid="forge-use-campaign-system"]').trigger('click')

    expect(campaignSystem(wrapper).exists()).toBe(true)
    expect(wrapper.find('[data-testid="forge-campaign-system-name"]').text()).toBe('Shadowrun Sixth World')
    await submit(wrapper)
    expect(sent().request).toMatchObject({ gameSystemDefinitionId: 'def-sr' })
  })

  it('keeps Generate disabled until the campaign is known, so a system is never picked for the wrong campaign', async () => {
    const { wrapper, resolveCampaigns } = await mountForge(PATH, { campaigns: 'pending' })
    const button = () => wrapper.find('button[type="submit"]')
    expect(button().attributes('disabled')).toBeDefined()

    resolveCampaigns([campaign({ gameSystemDefinitionId: 'def-sr' })])
    await flushPromises()

    expect(button().attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="forge-campaign-system-name"]').text()).toBe('Shadowrun Sixth World')
  })

  it('treats a campaign that is not found like no campaign: the default definition, "this campaign" in the banner', async () => {
    const { wrapper } = await mountForge('/adventures/generate?campaignId=missing', { campaigns: [campaign()] })

    expect(campaignSystem(wrapper).exists()).toBe(false)
    expect(selector(wrapper).element.value).toBe('def-dnd')
    expect(wrapper.find('[data-testid="forge-campaign"]').text()).toContain('this campaign')
  })
})

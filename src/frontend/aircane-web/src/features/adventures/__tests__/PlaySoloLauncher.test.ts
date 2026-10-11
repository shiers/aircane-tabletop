import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import PlaySoloLauncher from '../components/PlaySoloLauncher.vue'
import { useCampaignStore } from '@/features/campaigns/store'
import { useAdventureSessionStore } from '../stores/adventureSessionStore'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(),
  LogLevel: { Warning: 1 },
}))

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return { ...actual, getActiveAdventureSession: vi.fn() }
})

import * as api from '../api'

function campaign(overrides: Partial<CampaignDto> = {}): CampaignDto {
  return {
    id: 'campaign-1',
    name: 'Friday Night',
    gameSystem: 'D&D 5e',
    ruleset: '2014',
    gameSystemDefinitionId: null,
    aiRole: AiRole.FullDm,
    aiAuthority: AiAuthority.FullSessionControl,
    activeAdventureId: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    ...overrides,
  }
}

async function mountLauncher(campaigns: CampaignDto[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const campaignStore = useCampaignStore(pinia)
  vi.spyOn(campaignStore, 'fetchCampaigns').mockImplementation(async () => {
    campaignStore.campaigns = campaigns
  })
  const sessionStore = useAdventureSessionStore(pinia)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()

  const wrapper = mount(PlaySoloLauncher, {
    props: { adventureId: 'adventure-1' },
    global: { plugins: [pinia, router], stubs: { RouterLink: RouterLinkStub } },
  })
  return { wrapper, router, sessionStore }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('PlaySoloLauncher', () => {
  it('shows a Play Solo button and nothing else until clicked', async () => {
    const { wrapper } = await mountLauncher([campaign()])

    expect(wrapper.find('[data-testid="play-solo-button"]').text()).toContain('Play Solo')
    expect(wrapper.find('[data-testid="play-solo-panel"]').exists()).toBe(false)
  })

  it('with no campaigns, prompts the user to create one instead of offering Start a run', async () => {
    const { wrapper } = await mountLauncher([])

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="no-campaigns"]').text()).toContain('You need a campaign')
    expect(wrapper.findComponent(RouterLinkStub).props('to')).toBe('/campaigns')
    expect(wrapper.find('[data-testid="start-button"]').attributes('disabled')).toBeDefined()
  })

  it('with several campaigns, asks which to use and starts only after one is chosen', async () => {
    const { wrapper, sessionStore, router } = await mountLauncher([
      campaign(),
      campaign({ id: 'campaign-2', name: 'Solo Nights' }),
    ])
    const start = vi.spyOn(sessionStore, 'startSession').mockResolvedValue('run-9')

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="start-button"]').attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid="campaign-select"]').setValue('campaign-2')
    await wrapper.find('[data-testid="start-button"]').trigger('click')
    await flushPromises()

    expect(start).toHaveBeenCalledWith('adventure-1', 'campaign-2')
    expect(router.currentRoute.value.path).toBe('/adventures/solo/run-9')
  })

  it('preselects the only campaign', async () => {
    const { wrapper } = await mountLauncher([campaign()])

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()

    expect((wrapper.find('[data-testid="campaign-select"]').element as HTMLSelectElement).value).toBe('campaign-1')
    expect(wrapper.find('[data-testid="start-button"]').attributes('disabled')).toBeUndefined()
  })

  it('warns when the campaign\'s AI settings stop the AI DM from advancing the story', async () => {
    const { wrapper } = await mountLauncher([campaign({ aiRole: AiRole.Assistant, aiAuthority: AiAuthority.SuggestOnly })])

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="authority-hint"]').text()).toContain('Full Session Control')
  })

  it('shows no warning for a Full DM campaign with full session control', async () => {
    const { wrapper } = await mountLauncher([campaign()])

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="authority-hint"]').exists()).toBe(false)
  })

  it('on a 409 shows the server message and offers to resume the adventure already in progress', async () => {
    const { wrapper, sessionStore, router } = await mountLauncher([campaign()])
    vi.spyOn(sessionStore, 'startSession').mockImplementation(async () => {
      sessionStore.error = 'This campaign already has an adventure in progress.'
      throw Object.assign(new Error('conflict'), { response: { status: 409 } })
    })
    vi.mocked(api.getActiveAdventureSession).mockResolvedValue({ id: 'run-existing' } as never)

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()
    await wrapper.find('[data-testid="start-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="launcher-error"]').text()).toContain('already has an adventure in progress')

    await wrapper.find('[data-testid="resume-button"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/adventures/solo/run-existing')
  })

  it('shows the error and stays put when starting fails for another reason', async () => {
    const { wrapper, sessionStore, router } = await mountLauncher([campaign()])
    vi.spyOn(sessionStore, 'startSession').mockImplementation(async () => {
      sessionStore.error = 'Adventure is in \'Draft\' status; only approved adventures can be played.'
      throw new Error('bad request')
    })

    await wrapper.find('[data-testid="play-solo-button"]').trigger('click')
    await flushPromises()
    await wrapper.find('[data-testid="start-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="launcher-error"]').text()).toContain('only approved adventures')
    expect(wrapper.find('[data-testid="resume-button"]').exists()).toBe(false)
    expect(router.currentRoute.value.path).toBe('/')
  })
})

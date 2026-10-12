import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'
import HomeView from '../HomeView.vue'
import { useCampaignStore } from '@/features/campaigns/store'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'
import type { AdventureSessionDto } from '@/features/adventures/api'

vi.mock('@/features/adventures/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventures/api')>()),
  listInProgressAdventureSessions: vi.fn(),
}))

import * as adventuresApi from '@/features/adventures/api'

const campaign = (id: string, name: string, updatedAt: string): CampaignDto => ({
  id, name, gameSystem: 'D&D 5e', ruleset: '2014', gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm, aiAuthority: AiAuthority.FullSessionControl, activeAdventureId: null,
  createdAt: '2026-09-01T00:00:00Z', updatedAt,
})

const run = (id: string, campaignId: string, lastActivityAt: string): AdventureSessionDto => ({
  id, generatedAdventureId: 'adv', campaignId, sessionId: null, characterId: null, currentSceneId: 'scene-1',
  status: 'InProgress', outcomeNotes: null, startedAt: lastActivityAt, completedAt: null, lastActivityAt,
})

const Stub = defineComponent({ render: () => h('div') })

async function mountHome(campaigns: CampaignDto[], runs: AdventureSessionDto[] = []) {
  vi.mocked(adventuresApi.listInProgressAdventureSessions).mockResolvedValue(runs)
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useCampaignStore(pinia)
  vi.spyOn(store, 'fetchCampaigns').mockImplementation(async () => {
    store.campaigns = campaigns
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: HomeView },
      { path: '/campaigns', component: Stub },
      { path: '/campaigns/:campaignId', name: 'campaign-detail', component: Stub },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: Stub },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(HomeView, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return { wrapper, router }
}

const rows = (w: ReturnType<typeof mount>) => w.findAll('[data-testid="recent-campaign"]')

beforeEach(() => vi.clearAllMocks())

describe('Dashboard: Recent Campaigns', () => {
  it('Dashboard_ShowsAllCampaigns_NotJustWithSessions', async () => {
    // None of these campaigns has a play session, or a run; they must all still be listed.
    const { wrapper } = await mountHome([
      campaign('c1', 'Oldest', '2026-10-01T00:00:00Z'),
      campaign('c2', 'Newest', '2026-10-10T00:00:00Z'),
      campaign('c3', 'Middle', '2026-10-05T00:00:00Z'),
    ])

    expect(rows(wrapper).map((r) => r.find('[data-testid="recent-campaign-link"]').text())).toEqual(['Newest', 'Middle', 'Oldest'])
    expect(rows(wrapper)[0].text()).toContain('D&D 5e · 2014')
    expect(wrapper.find('[data-testid="resume-adventure-link"]').exists()).toBe(false)
  })

  it('shows at most five, and an empty dashboard has no list', async () => {
    const many = Array.from({ length: 8 }, (_, i) => campaign(`c${i}`, `Camp ${i}`, `2026-10-0${i + 1}T00:00:00Z`))
    expect(rows((await mountHome(many)).wrapper)).toHaveLength(5)

    const { wrapper } = await mountHome([])
    expect(wrapper.find('[data-testid="recent-campaigns"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('+ New Campaign')
  })

  it('links each campaign to its page', async () => {
    const { wrapper, router } = await mountHome([campaign('c1', 'Friday Night', '2026-10-01T00:00:00Z')])

    await wrapper.find('[data-testid="recent-campaign-link"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/campaigns/c1')
  })

  it('Dashboard_ShowsResumeAdventure_WhenActiveSession', async () => {
    const { wrapper, router } = await mountHome(
      [campaign('c1', 'Playing', '2026-10-01T00:00:00Z'), campaign('c2', 'Idle', '2026-10-02T00:00:00Z')],
      [run('run-9', 'c1', '2026-10-03T00:00:00Z')],
    )

    const [playing, idle] = rows(wrapper)
    // A run bumps its campaign to the top: it is the most recently active.
    expect(playing.find('[data-testid="recent-campaign-link"]').text()).toBe('Playing')
    expect(playing.find('[data-testid="resume-adventure-link"]').text()).toBe('Resume Adventure')
    expect(idle.find('[data-testid="resume-adventure-link"]').exists()).toBe(false)

    await playing.find('[data-testid="resume-adventure-link"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/adventures/solo/run-9')
  })

  it('uses the most recent run when a campaign has more than one in progress', async () => {
    const { wrapper } = await mountHome(
      [campaign('c1', 'Playing', '2026-10-01T00:00:00Z')],
      [run('old', 'c1', '2026-10-02T00:00:00Z'), run('new', 'c1', '2026-10-04T00:00:00Z')],
    )

    expect(wrapper.find('[data-testid="resume-adventure-link"]').attributes('href')).toBe('/adventures/solo/new')
  })

  it('still lists campaigns when the in-progress lookup fails', async () => {
    vi.mocked(adventuresApi.listInProgressAdventureSessions).mockRejectedValue(new Error('403'))
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useCampaignStore(pinia)
    vi.spyOn(store, 'fetchCampaigns').mockImplementation(async () => {
      store.campaigns = [campaign('c1', 'Solo', '2026-10-01T00:00:00Z')]
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: HomeView }, { path: '/campaigns', component: Stub }, { path: '/campaigns/:campaignId', name: 'campaign-detail', component: Stub }],
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(HomeView, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
    await flushPromises()

    expect(rows(wrapper)).toHaveLength(1)
  })
})

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { defineComponent, h } from 'vue'
import HomeView from '../HomeView.vue'
import { useCampaignStore } from '@/features/campaigns/store'
import { features } from '@/shared/features'

vi.mock('@/features/adventures/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/features/adventures/api')>()),
  listInProgressAdventureSessions: vi.fn().mockResolvedValue([]),
}))

const Stub = defineComponent({ render: () => h('div') })

async function mountHome() {
  const pinia = createPinia()
  setActivePinia(pinia)
  vi.spyOn(useCampaignStore(pinia), 'fetchCampaigns').mockResolvedValue(undefined)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: HomeView },
      ...['/campaigns', '/library', '/characters', '/sessions', '/settings/ai', '/adventures'].map((path) => ({ path, component: Stub })),
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(HomeView, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
  await flushPromises()
  return wrapper
}

const cardTitles = (w: ReturnType<typeof mount>) => w.findAll('h3').map((h) => h.text())
const quickActionLabels = (w: ReturnType<typeof mount>) => w.findAll('.quick-action-label').map((l) => l.text())
const hrefs = (w: ReturnType<typeof mount>) => w.findAll('a').map((a) => a.attributes('href'))

describe('Dashboard and the Characters feature flag', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })
  afterEach(() => {
    features.characters = true
  })

  it('with the feature on, shows the Character Sheets card and the Create Character action', async () => {
    features.characters = true

    const wrapper = await mountHome()

    expect(cardTitles(wrapper)).toContain('Character Sheets')
    expect(quickActionLabels(wrapper)).toContain('Create Character')
    expect(cardTitles(wrapper)).toHaveLength(6)
    expect(quickActionLabels(wrapper)).toHaveLength(4)
    expect(hrefs(wrapper).filter((href) => href === '/characters')).toHaveLength(2)
    expect(wrapper.find('.quick-action-card').element.parentElement?.className).toContain('lg:grid-cols-4')
  })

  it('with the feature off, has no Character Sheets card, no Create Character action and no link to /characters', async () => {
    features.characters = false

    const wrapper = await mountHome()

    expect(wrapper.text()).not.toContain('Character Sheets')
    expect(wrapper.text()).not.toContain('Create Character')
    expect(hrefs(wrapper)).not.toContain('/characters')
    expect(cardTitles(wrapper)).toEqual([
      'Document Library',
      'Campaign Management',
      'Join Session',
      'AI DM Runtime',
      'Adventure Generation',
    ])
    expect(quickActionLabels(wrapper)).toEqual(['Start AI DM', 'Import Documents', 'Roll Dice'])
    // Three actions fill their row instead of leaving a gap where the fourth was.
    expect(wrapper.find('.quick-action-card').element.parentElement?.className).toContain('lg:grid-cols-3')
  })
})

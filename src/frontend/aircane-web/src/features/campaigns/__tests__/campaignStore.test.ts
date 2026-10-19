import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useCampaignStore } from '../store'
import CampaignsView from '../CampaignsView.vue'
import * as api from '../api'
import { AiRole, AiAuthority, type CampaignDto } from '../api'

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  listCampaigns: vi.fn(),
}))

const campaign = (id: string): CampaignDto => ({
  id,
  name: `Campaign ${id}`,
  gameSystem: 'D&D 5e',
  ruleset: '2014',
  gameSystemDefinitionId: null,
  aiRole: AiRole.FullDm,
  aiAuthority: AiAuthority.FullSessionControl,
  activeAdventureId: null,
  createdAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
})

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
})

describe('campaign store: fetchCampaigns', () => {
  it('loads the campaigns', async () => {
    vi.mocked(api.listCampaigns).mockResolvedValue([campaign('a'), campaign('b')])
    const store = useCampaignStore()

    await store.fetchCampaigns()

    expect(store.campaigns.map((c) => c.id)).toEqual(['a', 'b'])
    expect(store.error).toBeNull()
    expect(store.loading).toBe(false)
  })

  // The bug: a failed load looked exactly like "no campaigns", so pickers said "You have no campaigns yet".
  it('rejects when the load fails, so callers can tell it from an empty list', async () => {
    vi.mocked(api.listCampaigns).mockRejectedValue(new Error('Network Error'))
    const store = useCampaignStore()

    await expect(store.fetchCampaigns()).rejects.toThrow('Network Error')

    expect(store.error).toBe('Network Error')
    expect(store.loading).toBe(false)
  })

  it('an empty result is not an error', async () => {
    vi.mocked(api.listCampaigns).mockResolvedValue([])
    const store = useCampaignStore()

    await expect(store.fetchCampaigns()).resolves.toBeUndefined()

    expect(store.campaigns).toEqual([])
    expect(store.error).toBeNull()
  })

  it('keeps the campaigns it already had when a refresh fails', async () => {
    vi.mocked(api.listCampaigns).mockResolvedValueOnce([campaign('a')]).mockRejectedValueOnce(new Error('offline'))
    const store = useCampaignStore()
    await store.fetchCampaigns()

    await expect(store.fetchCampaigns()).rejects.toThrow()

    expect(store.campaigns.map((c) => c.id)).toEqual(['a'])
  })

  it('clears the error on the next successful load', async () => {
    vi.mocked(api.listCampaigns).mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce([campaign('a')])
    const store = useCampaignStore()
    await expect(store.fetchCampaigns()).rejects.toThrow()

    await store.fetchCampaigns()

    expect(store.error).toBeNull()
  })
})

describe('CampaignsView with a failing load', () => {
  async function mountView() {
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(CampaignsView, { global: { plugins: [pinia, router], directives: { 'bg-asset': {} } } })
    await flushPromises()
    return wrapper
  }

  // The view shows store.error itself, so the rejection must not escape as an unhandled one
  // (vitest fails the run on those).
  it('shows the error instead of crashing', async () => {
    vi.mocked(api.listCampaigns).mockRejectedValue(new Error('Network Error'))

    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Network Error')
  })
})

import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import AppSidebar from '../AppSidebar.vue'
import vBgAsset from '@/directives/vBgAsset'
import { features } from '@/shared/features'

async function mountSidebar() {
  const stub = { template: '<div />' }
  const router = createRouter({
    history: createMemoryHistory(),
    routes: ['/', '/campaigns', '/adventures', '/sessions', '/library', '/characters', '/rules-lookup', '/settings/ai', '/about'].map(
      (path) => ({ path, component: stub }),
    ),
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(AppSidebar, {
    props: { collapsed: false },
    global: { plugins: [router], directives: { 'bg-asset': vBgAsset } },
  })
  await flushPromises()
  return wrapper
}

const links = (w: ReturnType<typeof mount>) => w.findAll('nav a').map((a) => a.text())

describe('AppSidebar and the Characters feature flag', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })
  afterEach(() => {
    features.characters = true
  })

  it('with the feature on, lists Characters and links it to /characters', async () => {
    features.characters = true

    const wrapper = await mountSidebar()

    expect(links(wrapper)).toContain('Characters')
    const item = wrapper.findAll('nav a').find((a) => a.text() === 'Characters')
    expect(item?.attributes('href')).toBe('/characters')
  })

  it('with the feature off, has no Characters item and keeps every other item, in order', async () => {
    features.characters = false

    const wrapper = await mountSidebar()

    expect(links(wrapper)).not.toContain('Characters')
    expect(wrapper.findAll('nav a').some((a) => a.attributes('href') === '/characters')).toBe(false)
    expect(links(wrapper)).toEqual([
      'Dashboard',
      'Campaigns',
      'Adventure Forge',
      'Join Session',
      'Library',
      'Rules Lookup',
      'AI DM',
      'About & Credits',
    ])
  })
})

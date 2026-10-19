import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createPinia } from 'pinia'
import AppLayout from '../AppLayout.vue'
import vBgAsset from '@/directives/vBgAsset'

vi.mock('../HealthStatus.vue', () => ({ default: { template: '<span />' } }))
vi.mock('@/features/feedback/FeedbackModal.vue', () => ({ default: { template: '<div />' } }))

/** Pretends the browser window is narrow (a phone) or wide (a desktop). */
function stubScreen(narrow: boolean): void {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: narrow && query.includes('max-width'),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  )
}

async function mountLayout() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/campaigns', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(AppLayout, {
    global: { plugins: [router, createPinia()], directives: { 'bg-asset': vBgAsset } },
  })
  await flushPromises()
  return { wrapper, router }
}

const aside = (w: ReturnType<typeof mount>) => w.find('aside')
const backdrop = (w: ReturnType<typeof mount>) => w.find('[data-testid="sidebar-backdrop"]')
const hamburger = (w: ReturnType<typeof mount>) => w.find('button[aria-label="Toggle sidebar"]')

beforeEach(() => {
  vi.unstubAllGlobals()
})
afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AppLayout sidebar on a narrow screen (390px)', () => {
  it('starts collapsed, hidden behind the hamburger button', async () => {
    stubScreen(true)

    const { wrapper } = await mountLayout()

    // Collapsed below the lg breakpoint means not shown at all, so the page gets the full width.
    expect(aside(wrapper).classes()).toContain('max-lg:hidden')
    expect(aside(wrapper).classes()).toContain('w-16')
    expect(backdrop(wrapper).exists()).toBe(false)
    expect(hamburger(wrapper).attributes('aria-expanded')).toBe('false')
  })

  it('opens as a drawer over the page when the hamburger is pressed', async () => {
    stubScreen(true)
    const { wrapper } = await mountLayout()

    await hamburger(wrapper).trigger('click')

    expect(aside(wrapper).classes()).not.toContain('max-lg:hidden')
    expect(aside(wrapper).classes()).toContain('max-lg:fixed')
    expect(aside(wrapper).classes()).toContain('max-lg:z-40')
    expect(backdrop(wrapper).exists()).toBe(true)
    expect(hamburger(wrapper).attributes('aria-expanded')).toBe('true')
  })

  it('closes when the backdrop is tapped', async () => {
    stubScreen(true)
    const { wrapper } = await mountLayout()
    await hamburger(wrapper).trigger('click')

    await backdrop(wrapper).trigger('click')

    expect(backdrop(wrapper).exists()).toBe(false)
    expect(aside(wrapper).classes()).toContain('max-lg:hidden')
  })

  it('closes after choosing a page', async () => {
    stubScreen(true)
    const { wrapper, router } = await mountLayout()
    await hamburger(wrapper).trigger('click')
    expect(backdrop(wrapper).exists()).toBe(true)

    await router.push('/campaigns')
    await flushPromises()

    expect(backdrop(wrapper).exists()).toBe(false)
    expect(aside(wrapper).classes()).toContain('max-lg:hidden')
  })
})

describe('AppLayout sidebar on a wide screen', () => {
  it('starts expanded, with no drawer backdrop', async () => {
    stubScreen(false)

    const { wrapper } = await mountLayout()

    expect(aside(wrapper).classes()).toContain('sidebar-expanded')
    expect(aside(wrapper).classes()).not.toContain('max-lg:hidden')
    // The backdrop is rendered while expanded but only visible below lg.
    expect(backdrop(wrapper).classes()).toContain('lg:hidden')
  })

  it('keeps the page open when navigating', async () => {
    stubScreen(false)
    const { wrapper, router } = await mountLayout()

    await router.push('/campaigns')
    await flushPromises()

    expect(aside(wrapper).classes()).toContain('sidebar-expanded')
  })

  it('still collapses to the icon strip with the collapse control', async () => {
    stubScreen(false)
    const { wrapper } = await mountLayout()

    await hamburger(wrapper).trigger('click')

    expect(aside(wrapper).classes()).toContain('w-16')
  })
})

describe('AppLayout when the browser cannot report the screen size', () => {
  it('starts expanded (the previous behaviour)', async () => {
    vi.stubGlobal('matchMedia', undefined)

    const { wrapper } = await mountLayout()

    expect(aside(wrapper).classes()).toContain('sidebar-expanded')
  })
})

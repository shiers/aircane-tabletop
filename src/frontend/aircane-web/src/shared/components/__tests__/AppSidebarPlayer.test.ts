import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import AppSidebar from '../AppSidebar.vue'
import vBgAsset from '@/directives/vBgAsset'
import { saveParticipantSession } from '@/features/sessions/participantSession'

/** A JWT-shaped token whose payload carries `exp` (seconds since the epoch). */
function tokenExpiring(expSeconds: number): string {
  return `h.${btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '')}.s`
}
const LIVE = tokenExpiring(Math.floor(Date.now() / 1000) + 3600)
const EXPIRED = tokenExpiring(Math.floor(Date.now() / 1000) - 3600)

function joinAsPlayer(token = LIVE, sessionId = 'session-1'): void {
  saveParticipantSession({ sessionId, participantId: 'participant-1', displayName: 'Thorin', token })
}

async function mountSidebar(path = '/') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sessions', component: { template: '<div />' } },
      { path: '/sessions/:sessionId/play', component: { template: '<div />' } },
      { path: '/campaigns', component: { template: '<div />' } },
    ],
  })
  await router.push(path)
  await router.isReady()
  const wrapper = mount(AppSidebar, {
    props: { collapsed: false },
    global: { plugins: [router], directives: { 'bg-asset': vBgAsset } },
  })
  await flushPromises()
  return { wrapper, router }
}

const links = (w: ReturnType<typeof mount>) => w.findAll('nav a').map((a) => a.text())

describe('AppSidebar for a host (no table of its own)', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('shows the full navigation, including "Join Session"', async () => {
    const { wrapper } = await mountSidebar()

    expect(links(wrapper)).toEqual(expect.arrayContaining(['Dashboard', 'Campaigns', 'Adventure Forge', 'Join Session', 'Library']))
    expect(wrapper.find('[data-testid="leave-table"]').exists()).toBe(false)
  })
})

describe('AppSidebar for a player who has joined a table', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  it('hides the host navigation and shows a single "Your session" link to the table', async () => {
    joinAsPlayer()

    const { wrapper } = await mountSidebar()

    expect(links(wrapper)).toEqual(['Your session'])
    expect(wrapper.find('nav a').attributes('href')).toBe('/sessions/session-1/play')
  })

  it('marks the link as the current page while at the table', async () => {
    joinAsPlayer()

    const { wrapper } = await mountSidebar('/sessions/session-1/play')

    expect(wrapper.find('nav a').attributes('aria-current')).toBe('page')
  })

  it('switches to the player navigation when the player joins, without a reload', async () => {
    const { wrapper, router } = await mountSidebar()
    expect(links(wrapper)).toContain('Campaigns')

    joinAsPlayer()
    await router.push('/sessions/session-1/play')
    await flushPromises()

    expect(links(wrapper)).toEqual(['Your session'])
  })

  it('goes back to the full navigation once the remembered login is gone', async () => {
    joinAsPlayer()
    const { wrapper, router } = await mountSidebar()
    expect(links(wrapper)).toEqual(['Your session'])

    sessionStorage.clear()   // refused by the server, or left
    await router.push('/campaigns')
    await flushPromises()

    expect(links(wrapper)).toContain('Campaigns')
  })

  it('ignores a login whose token has expired (and removes it)', async () => {
    joinAsPlayer(EXPIRED)

    const { wrapper } = await mountSidebar()

    expect(links(wrapper)).toContain('Campaigns')
    expect(sessionStorage.getItem('participant_token')).toBeNull()
  })

  it('has a way out: "Leave this table" forgets the login and shows the full navigation', async () => {
    joinAsPlayer()
    const { wrapper } = await mountSidebar('/sessions/session-1/play')

    await wrapper.find('[data-testid="leave-table"]').trigger('click')
    await flushPromises()

    expect(sessionStorage.getItem('participant_token')).toBeNull()
    expect(links(wrapper)).toContain('Campaigns')
  })
})

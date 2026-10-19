import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import JoinSessionView from '../JoinSessionView.vue'
import * as sessionsApi from '../api'
import vBgAsset from '@/directives/vBgAsset'

// The player has no token yet: the page must use only the anonymous join read.
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    getJoinInfo: vi.fn(),
    getSession: vi.fn(),
    joinSession: vi.fn(),
  }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRoute: () => ({ params: { sessionId: 'session-abc' } }) }
})

const mockedJoinInfo = vi.mocked(sessionsApi.getJoinInfo)
const mockedGetSession = vi.mocked(sessionsApi.getSession)

async function mountView() {
  const wrapper = mount(JoinSessionView, {
    global: {
      plugins: [createPinia()],
      directives: { 'bg-asset': vBgAsset },
      stubs: { RouterLink: true, QRCode: true },
    },
  })
  await flushPromises()
  return wrapper
}

describe('JoinSessionView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    sessionStorage.clear()
  })

  it('reads the anonymous join info, never the authenticated session', async () => {
    mockedJoinInfo.mockResolvedValue({ name: 'Friday Table', joinable: true })

    await mountView()

    expect(mockedJoinInfo).toHaveBeenCalledWith('session-abc')
    expect(mockedGetSession).not.toHaveBeenCalled()
  })

  it('shows the session name and no error for a joinable session', async () => {
    mockedJoinInfo.mockResolvedValue({ name: 'Friday Table', joinable: true })

    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Friday Table')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('shows the not-found panel, beside a working form, when the session is not joinable', async () => {
    mockedJoinInfo.mockResolvedValue({ name: null, joinable: false })

    const wrapper = await mountView()

    expect(wrapper.find('[role="alert"]').text()).toContain('no longer open')
    expect(wrapper.find('form').exists()).toBe(true)
  })

  it('shows a lookup error when the request itself fails', async () => {
    mockedJoinInfo.mockRejectedValue(new Error('Network Error'))

    const wrapper = await mountView()

    expect(wrapper.find('[role="alert"]').text()).toContain('Could not look up this session')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})

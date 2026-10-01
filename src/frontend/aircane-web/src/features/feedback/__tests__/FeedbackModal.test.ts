import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

// ── Mocks ──────────────────────────────────────────────────────────────────────
vi.mock('../api', () => ({
  submitFeedback: vi.fn(),
}))

const captureResult = {
  platform: 'Win32',
  userAgent: 'test',
  appVersion: '0.1.0',
  isTauri: false,
  tauriVersion: null,
  activeRoute: '/sessions/abc123/host',
  sessionId: 'abc123',
  campaignId: 'def456',
  userRole: null,
  aiProvider: 'Ollama',
  aiModel: 'llama3.2',
  embeddingProvider: null,
  embeddingModel: null,
}

vi.mock('../composables/useFeedbackDiagnostics', () => ({
  useFeedbackDiagnostics: () => ({
    capture: vi.fn().mockResolvedValue(captureResult),
    counts: () => ({ recentEvents: 3, consoleErrors: 2 }),
  }),
}))

vi.mock('../composables/useFeedbackEventBuffer', () => ({
  snapshot: () => ['12:00:00Z [Player] RollRecorded — 1d20 = 10'],
}))

vi.mock('../errorBuffer', () => ({
  getConsoleErrors: () => ['TypeError: boom'],
}))

vi.mock('@/shared/tauri/bridge', () => ({
  isDesktop: () => false,
  openExternal: vi.fn(),
}))

import FeedbackModal from '../FeedbackModal.vue'
import * as feedbackApi from '../api'

const mockedSubmit = vi.mocked(feedbackApi.submitFeedback)

function submitButton(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('button').find((b) => b.text().includes('Submit report'))
}

async function fillRequired(wrapper: ReturnType<typeof mount>) {
  await wrapper.find('#feedback-summary').setValue('AI stopped responding')
  await wrapper.find('#feedback-description').setValue('It just froze after initiative')
}

describe('FeedbackModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('disables Submit when Summary is empty', async () => {
    const wrapper = mount(FeedbackModal)
    await flushPromises()
    const btn = submitButton(wrapper)
    expect(btn).toBeTruthy()
    expect(btn!.attributes('disabled')).toBeDefined()
  })

  it('enables Submit when Summary and Description are filled', async () => {
    const wrapper = mount(FeedbackModal)
    await flushPromises()
    await fillRequired(wrapper)
    expect(submitButton(wrapper)!.attributes('disabled')).toBeUndefined()
  })

  it('renders the auto-captured diagnostic context', async () => {
    const wrapper = mount(FeedbackModal)
    await flushPromises()
    const diag = wrapper.find('[data-testid="diagnostic-context"]')
    expect(diag.exists()).toBe(true)
    expect(diag.text()).toContain('Ollama (llama3.2)')
    expect(diag.text()).toContain('/sessions/abc123/host')
    expect(diag.text()).toContain('abc123 (active)')
    expect(diag.text()).toContain('3 captured')
    expect(diag.text()).toContain('2 captured')
  })

  it('shows a success state with the GitHub issue link on submit', async () => {
    mockedSubmit.mockResolvedValue({
      success: true,
      issueUrl: 'https://github.com/owner/aircane-tabletop/issues/42',
      issueNumber: 42,
    })
    const wrapper = mount(FeedbackModal)
    await flushPromises()
    await fillRequired(wrapper)
    await submitButton(wrapper)!.trigger('click')
    await flushPromises()

    expect(mockedSubmit).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('Report submitted!')
    expect(wrapper.text()).toContain('Issue #42')
    const link = wrapper.find('a[href="https://github.com/owner/aircane-tabletop/issues/42"]')
    expect(link.exists()).toBe(true)
  })

  it('shows an error state with the Discord fallback when the API fails', async () => {
    mockedSubmit.mockRejectedValue({ response: { status: 503 } })
    const wrapper = mount(FeedbackModal)
    await flushPromises()
    await fillRequired(wrapper)
    await submitButton(wrapper)!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Could not submit — please try again or post in Discord.')
  })
})

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import SetupWizard from '../SetupWizard.vue'
import * as setupApi from '../api'
import * as aiApi from '@/features/ai/api'

// ── Mocks ────────────────────────────────────────────────────────────────────
vi.mock('../api', () => ({
  getSetupStatus: vi.fn(),
}))

vi.mock('@/features/ai/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/ai/api')>()
  return {
    ...actual,
    testAiConnection: vi.fn(),
    updateAiConfig: vi.fn(),
  }
})

vi.mock('@/shared/tauri/bridge', () => ({
  isDesktop: () => false,
  openExternal: vi.fn(),
}))

const mockedGetStatus = vi.mocked(setupApi.getSetupStatus)
const mockedTest = vi.mocked(aiApi.testAiConnection)
const mockedUpdate = vi.mocked(aiApi.updateAiConfig)

function statusResponse(overrides: Partial<setupApi.SetupStatus> = {}): setupApi.SetupStatus {
  return {
    isFirstLaunch: true,
    aiProviderConfigured: false,
    activeAiProvider: 'Fake',
    activeEmbeddingProvider: 'Fake',
    ollamaReachable: false,
    ...overrides,
  }
}

/** Finds a button whose text contains the given substring. */
function buttonByText(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().includes(text))
}

describe('SetupWizard flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
    mockedGetStatus.mockResolvedValue(statusResponse())
    // jsdom clipboard stub
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts on the welcome step', () => {
    const wrapper = mount(SetupWizard)
    expect(wrapper.text()).toContain('Aircane Tabletop')
    expect(buttonByText(wrapper, 'Get started')).toBeTruthy()
  })

  it('Skip on the welcome step sets completed flag and emits complete', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Skip setup')!.trigger('click')
    expect(localStorage.getItem('setup_wizard_completed')).toBe('true')
    expect(wrapper.emitted('complete')).toBeTruthy()
  })

  it('selecting Ollama navigates to the Ollama setup step', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    // Select Ollama (first click selects, Next proceeds)
    await wrapper.find('[data-provider="ollama"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('Set up Ollama')
  })

  it('selecting OpenAI navigates to the OpenAI setup step', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="openai"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    expect(wrapper.text()).toContain('Connect OpenAI')
  })

  it('selecting Try first navigates directly to the Done step and shows 3 total steps', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="fake"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    expect(wrapper.text()).toContain("You're all set")
    // Fake path reminder appears
    expect(wrapper.find('[data-testid="not-configured-reminder"]').exists()).toBe(true)
  })

  it('provider step shows "Step 2 of 4" before a provider is chosen', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    expect(wrapper.find('[data-testid="step-counter"]').text()).toBe('Step 2 of 4')
  })

  it('DoneStep shows the gentle reminder when provider is still Fake', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="fake"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    const reminder = wrapper.find('[data-testid="not-configured-reminder"]')
    expect(reminder.exists()).toBe(true)
    expect(reminder.text()).toContain('placeholder responses')
  })

  it('Start playing on Done sets completed flag and emits complete', async () => {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="fake"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    await buttonByText(wrapper, 'Start playing')!.trigger('click')
    expect(localStorage.getItem('setup_wizard_completed')).toBe('true')
    expect(wrapper.emitted('complete')).toBeTruthy()
  })
})

describe('OllamaSetupStep behaviour', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mockedGetStatus.mockResolvedValue(statusResponse({ ollamaReachable: false }))
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  async function gotoOllama() {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="ollama"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    await flushPromises()
    return wrapper
  }

  it('copy buttons write the correct commands to clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })

    const wrapper = await gotoOllama()
    const copyButtons = wrapper.findAll('button').filter((b) => b.text() === 'copy')
    expect(copyButtons.length).toBe(2)

    await copyButtons[0].trigger('click')
    await copyButtons[1].trigger('click')

    expect(writeText).toHaveBeenCalledWith('ollama pull llama3.2')
    expect(writeText).toHaveBeenCalledWith('ollama pull nomic-embed-text')
  })

  it('Continue is always enabled regardless of Ollama status', async () => {
    const wrapper = await gotoOllama()
    const cont = buttonByText(wrapper, 'Continue when ready')
    expect(cont).toBeTruthy()
    expect(cont!.attributes('disabled')).toBeUndefined()
  })

  it('shows a soft warning when continuing without Ollama detected, then proceeds', async () => {
    mockedGetStatus.mockResolvedValue(statusResponse({ ollamaReachable: false }))
    const wrapper = await gotoOllama()

    // First Continue click (not detected) reveals the soft warning and stays on the step.
    await buttonByText(wrapper, 'Continue when ready')!.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="ollama-not-detected-warning"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Set up Ollama')

    // Second click ("Continue anyway") proceeds to Done.
    await buttonByText(wrapper, 'Continue anyway')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain("You're all set")
  })

  it('saves Ollama as the active provider and advances when detected', async () => {
    mockedGetStatus.mockResolvedValue(statusResponse({ ollamaReachable: true }))
    const wrapper = await gotoOllama()
    await flushPromises()

    await buttonByText(wrapper, 'Continue when ready')!.trigger('click')
    await flushPromises()

    expect(mockedUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ activeProvider: aiApi.AiProviderType.Ollama }),
    )
    expect(wrapper.text()).toContain("You're all set")
    // Detected + configured => no "not configured" reminder.
    expect(wrapper.find('[data-testid="not-configured-reminder"]').exists()).toBe(false)
  })

  it('polls status and updates the indicator', async () => {
    vi.useFakeTimers()
    // First poll: not running; later: running.
    mockedGetStatus
      .mockResolvedValueOnce(statusResponse({ ollamaReachable: false }))
      .mockResolvedValue(statusResponse({ ollamaReachable: true }))

    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="ollama"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')

    // Let the immediate poll resolve.
    await vi.runOnlyPendingTimersAsync()
    await flushPromises()

    // Advance one 3s interval and let it resolve.
    await vi.advanceTimersByTimeAsync(3000)
    await flushPromises()

    const indicator = wrapper.find('[data-testid="ollama-status"]')
    expect(indicator.text()).toContain('Ollama is running')
  })
})

describe('OpenAiSetupStep behaviour', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mockedGetStatus.mockResolvedValue(statusResponse())
  })

  async function gotoOpenAi() {
    const wrapper = mount(SetupWizard)
    await buttonByText(wrapper, 'Get started')!.trigger('click')
    await wrapper.find('[data-provider="openai"]').trigger('click')
    await buttonByText(wrapper, 'Next')!.trigger('click')
    return wrapper
  }

  it('Save and continue is disabled until the test connection succeeds', async () => {
    mockedTest.mockResolvedValue({ success: true, model: 'gpt-4o' })

    const wrapper = await gotoOpenAi()
    // Initially disabled
    expect(buttonByText(wrapper, 'Save and continue')!.attributes('disabled')).toBeDefined()

    // Enter a key and test
    await wrapper.find('#setup-openai-key').setValue('sk-test')
    await buttonByText(wrapper, 'Test connection')!.trigger('click')
    await flushPromises()

    // Now enabled after a successful test
    expect(buttonByText(wrapper, 'Save and continue')!.attributes('disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="test-success"]').exists()).toBe(true)
  })

  it('keeps Save disabled when the test connection fails', async () => {
    mockedTest.mockResolvedValue({ success: false, message: 'Invalid key' })

    const wrapper = await gotoOpenAi()
    await wrapper.find('#setup-openai-key').setValue('sk-bad')
    await buttonByText(wrapper, 'Test connection')!.trigger('click')
    await flushPromises()

    expect(buttonByText(wrapper, 'Save and continue')!.attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="test-failure"]').exists()).toBe(true)
  })

  it('saves config and advances to Done on Save and continue', async () => {
    mockedTest.mockResolvedValue({ success: true, model: 'gpt-4o' })
    mockedUpdate.mockResolvedValue({ activeProvider: aiApi.AiProviderType.OpenAi })

    const wrapper = await gotoOpenAi()
    await wrapper.find('#setup-openai-key').setValue('sk-test')
    await buttonByText(wrapper, 'Test connection')!.trigger('click')
    await flushPromises()
    await buttonByText(wrapper, 'Save and continue')!.trigger('click')
    await flushPromises()

    expect(mockedUpdate).toHaveBeenCalledWith(
      expect.objectContaining({ activeProvider: aiApi.AiProviderType.OpenAi }),
    )
    expect(wrapper.text()).toContain("You're all set")
    // Configured OpenAI => no "not configured" reminder.
    expect(wrapper.find('[data-testid="not-configured-reminder"]').exists()).toBe(false)
  })

  it('Skip on OpenAI step goes to Done with the Fake reminder', async () => {
    const wrapper = await gotoOpenAi()
    await buttonByText(wrapper, 'Skip')!.trigger('click')
    expect(wrapper.text()).toContain("You're all set")
    expect(wrapper.find('[data-testid="not-configured-reminder"]').exists()).toBe(true)
  })
})

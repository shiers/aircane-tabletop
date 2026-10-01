import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useSetupWizard, WIZARD_COMPLETED_KEY } from '../composables/useSetupWizard'
import * as setupApi from '../api'

vi.mock('../api', () => ({
  getSetupStatus: vi.fn(),
}))

const mockedGetStatus = vi.mocked(setupApi.getSetupStatus)

function status(overrides: Partial<setupApi.SetupStatus> = {}): setupApi.SetupStatus {
  return {
    isFirstLaunch: true,
    aiProviderConfigured: false,
    activeAiProvider: 'Fake',
    activeEmbeddingProvider: 'Fake',
    ollamaReachable: false,
    ...overrides,
  }
}

describe('useSetupWizard.checkShouldShow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
    sessionStorage.clear()
  })

  it('shows on first launch when provider is Fake', async () => {
    mockedGetStatus.mockResolvedValue(status({ aiProviderConfigured: false }))
    const { checkShouldShow } = useSetupWizard()
    expect(await checkShouldShow()).toBe(true)
  })

  it('does not show when setup_wizard_completed is true', async () => {
    localStorage.setItem(WIZARD_COMPLETED_KEY, 'true')
    const { checkShouldShow } = useSetupWizard()
    expect(await checkShouldShow()).toBe(false)
    // Should not even call the backend once the flag is set.
    expect(mockedGetStatus).not.toHaveBeenCalled()
  })

  it('does not show when a non-Fake provider is configured', async () => {
    mockedGetStatus.mockResolvedValue(
      status({ aiProviderConfigured: true, activeAiProvider: 'Ollama' }),
    )
    const { checkShouldShow } = useSetupWizard()
    expect(await checkShouldShow()).toBe(false)
  })

  it('does not show for a Player browser (participant token present)', async () => {
    sessionStorage.setItem('participant_token', 'jwt-token')
    const { checkShouldShow } = useSetupWizard()
    expect(await checkShouldShow()).toBe(false)
    expect(mockedGetStatus).not.toHaveBeenCalled()
  })

  it('fails safe (does not show) when the status request errors', async () => {
    mockedGetStatus.mockRejectedValue(new Error('network'))
    const { checkShouldShow } = useSetupWizard()
    expect(await checkShouldShow()).toBe(false)
  })

  it('skip() sets the completed flag and hides the wizard', () => {
    const { skip, isVisible, open } = useSetupWizard()
    open()
    expect(isVisible.value).toBe(true)
    skip()
    expect(localStorage.getItem(WIZARD_COMPLETED_KEY)).toBe('true')
    expect(isVisible.value).toBe(false)
  })

  it('complete() sets the completed flag and hides the wizard', () => {
    const { complete, isVisible, open } = useSetupWizard()
    open()
    complete()
    expect(localStorage.getItem(WIZARD_COMPLETED_KEY)).toBe('true')
    expect(isVisible.value).toBe(false)
  })
})

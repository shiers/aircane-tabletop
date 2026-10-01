import { ref } from 'vue'
import { getSetupStatus } from '../api'

/** Steps in the wizard flow. */
export type WizardStep = 'welcome' | 'provider' | 'ollama' | 'openai' | 'done'

/** The provider the host chose on the provider-selection step. */
export type SelectedProvider = 'ollama' | 'openai' | 'fake' | null

export const WIZARD_COMPLETED_KEY = 'setup_wizard_completed'
const PARTICIPANT_TOKEN_KEY = 'participant_token'

/**
 * Drives the first-launch setup wizard: which step is showing, the chosen
 * provider, visibility, and the completion flag persisted in localStorage.
 *
 * The wizard shows at most once per browser/device: once completed or skipped,
 * the `setup_wizard_completed` flag suppresses it on future launches even if the
 * provider is still Fake (the user may have deliberately chosen to configure later).
 */
export function useSetupWizard() {
  const currentStep = ref<WizardStep>('welcome')
  const selectedProvider = ref<SelectedProvider>(null)
  const isVisible = ref(false)

  /**
   * Decides whether the wizard should appear on startup.
   * Returns false (skip) when:
   *  - the wizard was already completed/skipped on this device, or
   *  - this is a Player browser (a participant token exists — players never see setup), or
   *  - a non-Fake AI provider is already configured, or
   *  - the status request fails (fail safe: never block the app on a wizard).
   */
  async function checkShouldShow(): Promise<boolean> {
    if (localStorage.getItem(WIZARD_COMPLETED_KEY) === 'true') return false

    // Player browsers (joined a session) must never see the host setup wizard.
    if (sessionStorage.getItem(PARTICIPANT_TOKEN_KEY) !== null) return false

    try {
      const status = await getSetupStatus()
      // Show when the AI provider hasn't been configured (still the Fake default).
      // isFirstLaunch (no ai-settings.json) implies not configured too.
      return !status.aiProviderConfigured
    } catch {
      // If we can't reach the backend, don't trap the user behind the wizard.
      return false
    }
  }

  /** Show the wizard, resetting to the first step. */
  function open() {
    currentStep.value = 'welcome'
    selectedProvider.value = null
    isVisible.value = true
  }

  /** Marks setup complete for this device and hides the wizard. */
  function complete() {
    localStorage.setItem(WIZARD_COMPLETED_KEY, 'true')
    isVisible.value = false
  }

  /** Skipping is the same as completing — we don't nag on future launches. */
  function skip() {
    complete()
  }

  function goTo(step: WizardStep) {
    currentStep.value = step
  }

  return {
    currentStep,
    selectedProvider,
    isVisible,
    checkShouldShow,
    open,
    complete,
    skip,
    goTo,
  }
}

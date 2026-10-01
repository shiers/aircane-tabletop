<script setup lang="ts">
/**
 * SetupWizard — fullscreen first-launch modal that guides the host through
 * choosing an AI provider. Cannot be dismissed by clicking outside; it must be
 * completed or explicitly skipped.
 *
 * Flow:
 *   welcome → provider → (ollama | openai) → done
 *   "Try first, configure later" on the provider step skips straight to done,
 *   so that path is 3 steps total (Step 2 of 3), while ollama/openai are 4.
 */
import { computed, ref } from 'vue'
import { useSetupWizard, type SelectedProvider } from './composables/useSetupWizard'
import WelcomeStep from './steps/WelcomeStep.vue'
import ProviderChoiceStep from './steps/ProviderChoiceStep.vue'
import OllamaSetupStep from './steps/OllamaSetupStep.vue'
import OpenAiSetupStep from './steps/OpenAiSetupStep.vue'
import DoneStep from './steps/DoneStep.vue'

const emit = defineEmits<{
  (e: 'complete'): void
}>()

const { currentStep, selectedProvider, complete, skip, goTo } = useSetupWizard()

// The wizard is always visible while mounted — App.vue controls mounting via v-if.
// Start on the welcome step.
currentStep.value = 'welcome'

// Whether Ollama was detected when the host left the Ollama step (drives the Done summary).
const ollamaDetected = ref(false)
// True only when a real provider was actually configured (OpenAI saved, or Ollama
// continued). Skipping a provider step leaves this false so Done reflects "Fake".
const providerConfigured = ref(false)

// ── Step header: number + total ────────────────────────────────────────────
// The "fake" (Try first) path skips the provider-setup step, so it's 3 steps
// total; ollama/openai paths are 4.
const totalSteps = computed(() => (selectedProvider.value === 'fake' ? 3 : 4))

const stepNumber = computed(() => {
  switch (currentStep.value) {
    case 'welcome':
      return 1
    case 'provider':
      return 2
    case 'ollama':
    case 'openai':
      return 3
    case 'done':
      // Done is the last step: 3 on the fake path, 4 otherwise.
      return totalSteps.value
    default:
      return 1
  }
})

// Only show the "Step X of Y" header on the numbered middle steps.
const showStepCounter = computed(
  () => currentStep.value !== 'welcome' && currentStep.value !== 'done',
)

// ── Navigation handlers ──────────────────────────────────────────────────────
function onProviderChosen(provider: Exclude<SelectedProvider, null>) {
  selectedProvider.value = provider
  if (provider === 'ollama') goTo('ollama')
  else if (provider === 'openai') goTo('openai')
  else goTo('done') // fake / try-first
}

function onOllamaContinue(detected: boolean) {
  ollamaDetected.value = detected
  // Continuing on the Ollama path configures Ollama (whether or not it's detected
  // yet — the user may still be pulling models). Skipping does not.
  providerConfigured.value = true
  goTo('done')
}

function onOpenAiSaved() {
  providerConfigured.value = true
  goTo('done')
}

// Skipping either provider setup step: the effective provider is Fake.
function onProviderSkip() {
  selectedProvider.value = 'fake'
  providerConfigured.value = false
  goTo('done')
}

function onFinish() {
  complete()
  emit('complete')
}

function onSkip() {
  skip()
  emit('complete')
}
</script>

<template>
  <div
    class="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 p-4"
    role="dialog"
    aria-modal="true"
    aria-label="First-time setup"
    data-testid="setup-wizard"
  >
    <div class="w-full max-w-lg rounded-2xl border border-gray-800 bg-gray-900 p-8 shadow-2xl">
      <!-- Step counter header (hidden on welcome/done) -->
      <div
        v-if="showStepCounter"
        class="mb-6 flex items-center justify-between text-xs text-gray-500"
      >
        <span>&nbsp;</span>
        <span data-testid="step-counter">Step {{ stepNumber }} of {{ totalSteps }}</span>
      </div>

      <WelcomeStep
        v-if="currentStep === 'welcome'"
        @next="goTo('provider')"
        @skip="onSkip"
      />

      <ProviderChoiceStep
        v-else-if="currentStep === 'provider'"
        v-model="selectedProvider"
        @back="goTo('welcome')"
        @choose="onProviderChosen"
      />

      <OllamaSetupStep
        v-else-if="currentStep === 'ollama'"
        @back="goTo('provider')"
        @continue="onOllamaContinue"
        @skip="onProviderSkip"
      />

      <OpenAiSetupStep
        v-else-if="currentStep === 'openai'"
        @back="goTo('provider')"
        @saved="onOpenAiSaved"
        @skip="onProviderSkip"
      />

      <DoneStep
        v-else-if="currentStep === 'done'"
        :provider="selectedProvider"
        :configured="providerConfigured"
        :ollama-detected="ollamaDetected"
        @finish="onFinish"
      />
    </div>
  </div>
</template>

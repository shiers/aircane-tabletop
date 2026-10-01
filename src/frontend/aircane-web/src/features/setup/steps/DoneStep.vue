<script setup lang="ts">
/**
 * Step 4 — Done. Summarises what was configured and shows a gentle reminder when
 * the provider is still Fake or Ollama wasn't detected.
 */
import { computed } from 'vue'
import type { SelectedProvider } from '../composables/useSetupWizard'

const props = defineProps<{
  provider: SelectedProvider
  /** True when a real provider was actually configured (OpenAI saved / Ollama continued). */
  configured: boolean
  /** Whether Ollama was detected at the moment the host continued (Ollama path only). */
  ollamaDetected?: boolean
}>()

defineEmits<{
  (e: 'finish'): void
}>()

// The effective provider label: only show a real provider if it was configured;
// a skipped provider step means the app runs on Fake.
const providerLabel = computed(() => {
  if (!props.configured) return 'Placeholder (Fake)'
  switch (props.provider) {
    case 'ollama':
      return 'Ollama (local)'
    case 'openai':
      return 'OpenAI'
    default:
      return 'Placeholder (Fake)'
  }
})

// "Fully configured" means a real provider was set up AND, for Ollama, it was
// actually detected (OpenAI reaching Done means the key was saved+tested).
const fullyConfigured = computed(() => {
  if (!props.configured) return false
  if (props.provider === 'ollama') return props.ollamaDetected === true
  return props.provider === 'openai'
})

const connectionNote = computed(() => {
  if (!props.configured) return null
  if (props.provider === 'openai') return '✓ Connected'
  if (props.provider === 'ollama') {
    return props.ollamaDetected ? '✓ Connected' : 'Not detected yet'
  }
  return null
})
</script>

<template>
  <div class="flex flex-col items-center text-center gap-6">
    <div
      class="flex h-14 w-14 items-center justify-center rounded-full bg-green-600/20 text-2xl text-green-400"
      aria-hidden="true"
    >
      ✓
    </div>

    <h2 class="text-xl font-semibold text-white">You're all set</h2>

    <div class="space-y-1 text-sm">
      <p class="text-gray-300">
        AI provider: <span class="font-medium text-white">{{ providerLabel }}</span>
      </p>
      <p
        v-if="connectionNote"
        :class="fullyConfigured ? 'text-green-400' : 'text-amber-400'"
      >
        {{ connectionNote }}
      </p>
    </div>

    <p class="text-sm text-gray-400">
      You can change this any time in Settings → AI Provider.
    </p>

    <!-- Gentle reminder when AI isn't fully configured. -->
    <p
      v-if="!fullyConfigured"
      class="max-w-sm rounded-lg border border-amber-800 bg-amber-950/40 px-4 py-3 text-sm text-amber-300"
      data-testid="not-configured-reminder"
    >
      AI is not fully configured yet. The app will use placeholder responses until you set up
      a provider in Settings → AI Provider.
    </p>

    <button
      type="button"
      class="rounded-lg bg-aircane-600 px-6 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
      @click="$emit('finish')"
    >
      Start playing →
    </button>
  </div>
</template>

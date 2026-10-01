<script setup lang="ts">
/**
 * Step 3b — OpenAI setup. Enter an API key, test it, then save. "Save and continue"
 * is disabled until a test connection succeeds. The key is saved server-side via
 * PUT /api/ai/settings and never stored in the frontend.
 */
import { ref } from 'vue'
import {
  AiProviderType,
  testAiConnection,
  updateAiConfig,
  type TestConnectionResult,
} from '@/features/ai/api'
import { isDesktop, openExternal } from '@/shared/tauri/bridge'

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'saved'): void
  (e: 'skip'): void
}>()

const OPENAI_KEYS_URL = 'https://platform.openai.com/api-keys'
const DEFAULT_MODEL = 'gpt-4o-mini'

const apiKey = ref('')
const showKey = ref(false)
const testing = ref(false)
const saving = ref(false)
const testResult = ref<TestConnectionResult | null>(null)
const saveError = ref<string | null>(null)

function openKeysPage() {
  if (isDesktop()) {
    void openExternal(OPENAI_KEYS_URL)
  } else {
    window.open(OPENAI_KEYS_URL, '_blank', 'noopener')
  }
}

function clearKey() {
  apiKey.value = ''
  testResult.value = null
}

async function testConnection() {
  testing.value = true
  testResult.value = null
  saveError.value = null
  try {
    testResult.value = await testAiConnection({
      activeProvider: AiProviderType.OpenAi,
      openAi: { apiKey: apiKey.value, model: DEFAULT_MODEL },
    })
  } catch (e: any) {
    testResult.value = {
      success: false,
      message: e?.response?.data?.message ?? e?.message ?? 'Connection test failed',
    }
  } finally {
    testing.value = false
  }
}

async function saveAndContinue() {
  saving.value = true
  saveError.value = null
  try {
    await updateAiConfig({
      activeProvider: AiProviderType.OpenAi,
      openAi: { apiKey: apiKey.value, model: DEFAULT_MODEL },
    })
    emit('saved')
  } catch (e: any) {
    saveError.value = e?.response?.data?.error ?? e?.message ?? 'Failed to save settings'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="space-y-5">
    <div>
      <h2 class="text-xl font-semibold text-white">Connect OpenAI</h2>
      <p class="mt-1 text-sm text-gray-400">
        You'll need an OpenAI API key. Get one at platform.openai.com/api-keys.
      </p>
      <button
        type="button"
        class="mt-2 rounded-lg border border-gray-600 bg-gray-800 px-4 py-1.5 text-sm font-medium text-gray-100 hover:border-gray-500"
        @click="openKeysPage"
      >
        Open OpenAI →
      </button>
    </div>

    <!-- API key -->
    <div class="space-y-1">
      <label for="setup-openai-key" class="block text-sm font-medium text-gray-300">API Key</label>
      <div class="flex items-center gap-2">
        <input
          id="setup-openai-key"
          v-model="apiKey"
          :type="showKey ? 'text' : 'password'"
          placeholder="sk-..."
          autocomplete="off"
          class="w-full rounded-md border border-gray-700 bg-gray-950 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500"
        />
        <button
          type="button"
          :aria-label="showKey ? 'Hide key' : 'Show key'"
          class="rounded-md border border-gray-700 px-2 py-2 text-sm text-gray-300 hover:bg-gray-800"
          @click="showKey = !showKey"
        >
          👁
        </button>
        <button
          type="button"
          aria-label="Clear key"
          class="rounded-md border border-gray-700 px-2 py-2 text-sm text-gray-300 hover:bg-gray-800"
          @click="clearKey"
        >
          ✕
        </button>
      </div>
    </div>

    <!-- Test connection -->
    <div class="space-y-2">
      <button
        type="button"
        :disabled="testing || !apiKey"
        class="rounded-lg border border-gray-600 bg-gray-800 px-4 py-1.5 text-sm font-semibold text-gray-100 hover:border-gray-500 disabled:cursor-not-allowed disabled:opacity-50"
        @click="testConnection"
      >
        {{ testing ? 'Testing…' : 'Test connection' }}
      </button>

      <p
        v-if="testResult?.success"
        class="text-sm text-green-400"
        data-testid="test-success"
      >
        ✓ Connected{{ testResult.model ? ` — ${testResult.model} available` : '' }}
      </p>
      <p
        v-else-if="testResult && !testResult.success"
        class="text-sm text-red-400"
        data-testid="test-failure"
      >
        ✗ {{ testResult.message ?? 'Invalid key — check and try again' }}
      </p>
    </div>

    <p class="rounded-lg bg-gray-950 px-3 py-2 text-xs text-gray-400">
      Note: API usage costs money. Typical session cost is $0.05–$0.50 depending on session
      length and model.
    </p>

    <p v-if="saveError" class="text-sm text-red-400">{{ saveError }}</p>

    <!-- Actions -->
    <div class="flex items-center justify-between pt-2">
      <button
        type="button"
        class="text-sm text-gray-400 hover:text-gray-200"
        @click="$emit('back')"
      >
        ← Back
      </button>
      <div class="flex flex-col items-end gap-2">
        <button
          type="button"
          :disabled="!testResult?.success || saving"
          class="rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
          @click="saveAndContinue"
        >
          {{ saving ? 'Saving…' : 'Save and continue →' }}
        </button>
        <button
          type="button"
          class="text-xs text-gray-500 hover:text-gray-300 hover:underline"
          @click="$emit('skip')"
        >
          Skip — configure later
        </button>
      </div>
    </div>
  </div>
</template>

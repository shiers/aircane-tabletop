<script setup lang="ts">
/**
 * Step 3a — Ollama setup. Download link (system browser), copy-able pull commands,
 * a live connection indicator (polls every 3s), and an always-enabled Continue.
 */
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useOllamaPoller } from '../composables/useOllamaPoller'
import { AiProviderType, updateAiConfig } from '@/features/ai/api'
import { isDesktop, openExternal } from '@/shared/tauri/bridge'

const emit = defineEmits<{
  (e: 'back'): void
  // continue-with carries whether Ollama was detected, so the shell can tailor
  // the Done step.
  (e: 'continue', ollamaDetected: boolean): void
  (e: 'skip'): void
}>()

const OLLAMA_DOWNLOAD_URL = 'https://ollama.com/download'
const OLLAMA_DEFAULT_BASE_URL = 'http://localhost:11434'
const OLLAMA_DEFAULT_MODEL = 'llama3.2'

const commands = [
  { cmd: 'ollama pull llama3.2', note: 'AI Dungeon Master, narration, rules answers (~2GB)' },
  { cmd: 'ollama pull nomic-embed-text', note: 'RAG retrieval for your rules library (~270MB)' },
]

const copiedCmd = ref<string | null>(null)
// When the host clicks Continue while Ollama isn't detected, we surface a soft
// warning and let them acknowledge it (a second click proceeds).
const showNotDetectedWarning = ref(false)
const saving = ref(false)

const { status, start, stop } = useOllamaPoller()

onMounted(() => {
  start()
})

onBeforeUnmount(() => {
  stop()
})

function openDownload() {
  if (isDesktop()) {
    void openExternal(OLLAMA_DOWNLOAD_URL)
  } else {
    window.open(OLLAMA_DOWNLOAD_URL, '_blank', 'noopener')
  }
}

async function copy(cmd: string) {
  try {
    await navigator.clipboard.writeText(cmd)
    copiedCmd.value = cmd
    setTimeout(() => {
      if (copiedCmd.value === cmd) copiedCmd.value = null
    }, 1500)
  } catch {
    // Clipboard unavailable — ignore.
  }
}

/**
 * Persists Ollama as the active chat provider, then advances to Done.
 *
 * Note on embeddings: the embedding provider (Embeddings:Provider) is resolved at
 * server startup from configuration and is not settable through the AI settings
 * API at runtime, so we persist the chat provider here. The RAG/embedding side
 * uses Ollama when the backend is configured with Embeddings:Provider=Ollama
 * (documented in the AI configuration guide).
 */
async function saveOllamaAndContinue(detected: boolean) {
  saving.value = true
  try {
    await updateAiConfig({
      activeProvider: AiProviderType.Ollama,
      ollama: { baseUrl: OLLAMA_DEFAULT_BASE_URL, model: OLLAMA_DEFAULT_MODEL },
    })
  } catch {
    // Non-fatal: even if saving fails, let the user proceed rather than trapping
    // them in the wizard. They can retry from Settings → AI Provider.
  } finally {
    saving.value = false
  }
  stop()
  emit('continue', detected)
}

function onContinue() {
  if (status.value === 'running') {
    void saveOllamaAndContinue(true)
    return
  }
  // Not detected: first click reveals the soft warning; a second click proceeds.
  if (!showNotDetectedWarning.value) {
    showNotDetectedWarning.value = true
    return
  }
  void saveOllamaAndContinue(false)
}

function onSkip() {
  stop()
  emit('skip')
}
</script>

<template>
  <div class="space-y-5">
    <div>
      <h2 class="text-xl font-semibold text-white">Set up Ollama</h2>
      <p class="mt-1 text-sm text-gray-400">
        Ollama runs open-source AI models locally on your machine. It needs to be installed
        separately — it's free and takes about 5 minutes.
      </p>
    </div>

    <!-- 1. Install -->
    <div class="space-y-2">
      <p class="text-sm font-medium text-gray-200">1. Install Ollama</p>
      <button
        type="button"
        class="rounded-lg border border-gray-600 bg-gray-800 px-4 py-1.5 text-sm font-medium text-gray-100 hover:border-gray-500"
        @click="openDownload"
      >
        Download Ollama →
      </button>
    </div>

    <!-- 2. Pull models -->
    <div class="space-y-2">
      <p class="text-sm font-medium text-gray-200">2. Pull the required models</p>
      <p class="text-xs text-gray-500">Open a terminal and run:</p>
      <ul class="space-y-2">
        <li
          v-for="c in commands"
          :key="c.cmd"
          class="rounded-lg bg-gray-950 px-3 py-2"
        >
          <div class="flex items-center justify-between gap-3">
            <code class="font-mono text-sm text-gray-200">{{ c.cmd }}</code>
            <button
              type="button"
              class="rounded-md border border-gray-700 px-2 py-1 text-xs text-gray-300 hover:bg-gray-800"
              @click="copy(c.cmd)"
            >
              {{ copiedCmd === c.cmd ? 'Copied' : 'copy' }}
            </button>
          </div>
          <p class="mt-1 text-xs text-gray-500">{{ c.note }}</p>
        </li>
      </ul>
      <p class="text-xs text-gray-500">
        Total: ~2.3GB download. Ollama downloads models in the background after
        <code>pull</code>.
      </p>
    </div>

    <!-- 3. Connection status -->
    <div class="space-y-2">
      <p class="text-sm font-medium text-gray-200">3. Check connection</p>
      <div class="flex items-center gap-2 text-sm" data-testid="ollama-status">
        <span
          class="inline-block h-2.5 w-2.5 rounded-full"
          :class="{
            'bg-gray-500 animate-pulse': status === 'checking',
            'bg-green-400': status === 'running',
            'bg-amber-400': status === 'not-running',
          }"
          aria-hidden="true"
        ></span>
        <span
          :class="{
            'text-gray-400': status === 'checking',
            'text-green-400': status === 'running',
            'text-amber-400': status === 'not-running',
          }"
        >
          <template v-if="status === 'checking'">Checking…</template>
          <template v-else-if="status === 'running'">✓ Ollama is running</template>
          <template v-else>✗ Not detected — is Ollama running?</template>
        </span>
      </div>
    </div>

    <!-- Soft warning shown when the host continues without Ollama detected. -->
    <p
      v-if="showNotDetectedWarning && status !== 'running'"
      class="rounded-lg border border-amber-800 bg-amber-950/40 px-3 py-2 text-sm text-amber-300"
      data-testid="ollama-not-detected-warning"
    >
      Ollama wasn't detected. You can finish setup and start Ollama before your first session.
    </p>

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
          :disabled="saving"
          class="rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 disabled:opacity-50"
          @click="onContinue"
        >
          {{
            saving
              ? 'Saving…'
              : showNotDetectedWarning && status !== 'running'
                ? 'Continue anyway →'
                : 'Continue when ready →'
          }}
        </button>
        <button
          type="button"
          class="text-xs text-gray-500 hover:text-gray-300 hover:underline"
          @click="onSkip"
        >
          Skip — configure Ollama later
        </button>
      </div>
    </div>
  </div>
</template>

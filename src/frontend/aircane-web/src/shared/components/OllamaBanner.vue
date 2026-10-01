<script setup lang="ts">
/**
 * OllamaBanner
 *
 * Shown only inside the desktop wrapper, and only when the Rust side detects
 * that Ollama is the configured AI provider but the daemon isn't running. The
 * wrapper emits the `ollama://not-running` event; we listen for it and surface a
 * non-blocking, dismissible banner. The app remains fully usable without Ollama
 * (the Fake provider works offline), so this never blocks interaction.
 */
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { isDesktop, listen, openExternal, TauriEvents } from '@/shared/tauri/bridge'

const router = useRouter()

const visible = ref(false)
const commandsModalOpen = ref(false)

const OLLAMA_URL = 'https://ollama.com'
const commands = [
  'ollama serve',
  'ollama pull llama3.2',
  'ollama pull nomic-embed-text',
]
const copiedIndex = ref<number | null>(null)

let unlisten: (() => void) | null = null

onMounted(async () => {
  // Nothing to do in a plain browser — the event never fires there anyway,
  // but we short-circuit to avoid registering a listener.
  if (!isDesktop()) return
  unlisten = await listen(TauriEvents.OllamaNotRunning, () => {
    visible.value = true
  })
})

onUnmounted(() => {
  unlisten?.()
})

function startOllama() {
  // Open the download page in the system browser and reveal the setup commands.
  openExternal(OLLAMA_URL)
  commandsModalOpen.value = true
}

function useOpenAiInstead() {
  visible.value = false
  router.push({ name: 'ai-settings' })
}

function dismiss() {
  // Hides for the session only (not persisted).
  visible.value = false
}

async function copyCommand(cmd: string, index: number) {
  try {
    await navigator.clipboard.writeText(cmd)
    copiedIndex.value = index
    setTimeout(() => {
      if (copiedIndex.value === index) copiedIndex.value = null
    }, 1500)
  } catch {
    // Clipboard unavailable — ignore silently.
  }
}
</script>

<template>
  <div v-if="visible" role="status" class="border-b border-amber-700 bg-amber-950/80 px-4 py-3">
    <div class="mx-auto flex max-w-5xl flex-wrap items-center gap-x-3 gap-y-2 text-sm text-amber-100">
      <span class="font-semibold">Ollama isn't running.</span>
      <span class="text-amber-200/90">
        The AI DM and RAG retrieval need Ollama for local AI.
      </span>
      <span class="ml-auto flex flex-wrap gap-2">
        <button
          class="rounded-md bg-amber-600 px-3 py-1 font-medium text-white hover:bg-amber-500"
          @click="startOllama"
        >
          Start Ollama
        </button>
        <button
          class="rounded-md border border-amber-500 px-3 py-1 font-medium text-amber-100 hover:bg-amber-900/60"
          @click="useOpenAiInstead"
        >
          Use OpenAI instead
        </button>
        <button
          class="rounded-md px-3 py-1 font-medium text-amber-200 hover:bg-amber-900/60"
          @click="dismiss"
        >
          Dismiss
        </button>
      </span>
    </div>
  </div>

  <!-- Setup commands modal -->
  <div
    v-if="commandsModalOpen"
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
    @click.self="commandsModalOpen = false"
  >
    <div class="w-full max-w-md rounded-xl border border-gray-700 bg-gray-900 p-6 shadow-xl">
      <h2 class="text-lg font-semibold text-white">Get Ollama running</h2>
      <p class="mt-1 text-sm text-gray-400">
        After installing Ollama, run these commands in a terminal. Aircane will pick
        it up automatically once it's serving.
      </p>
      <ul class="mt-4 space-y-2">
        <li
          v-for="(cmd, i) in commands"
          :key="cmd"
          class="flex items-center justify-between gap-3 rounded-lg bg-gray-950 px-3 py-2 font-mono text-sm text-gray-200"
        >
          <code>{{ cmd }}</code>
          <button
            class="rounded-md border border-gray-700 px-2 py-1 text-xs text-gray-300 hover:bg-gray-800"
            @click="copyCommand(cmd, i)"
          >
            {{ copiedIndex === i ? 'Copied' : 'Copy' }}
          </button>
        </li>
      </ul>
      <div class="mt-5 flex justify-end">
        <button
          class="rounded-md bg-gray-700 px-4 py-1.5 text-sm font-medium text-white hover:bg-gray-600"
          @click="commandsModalOpen = false"
        >
          Close
        </button>
      </div>
    </div>
  </div>
</template>

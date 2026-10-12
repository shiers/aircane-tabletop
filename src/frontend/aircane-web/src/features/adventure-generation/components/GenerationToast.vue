<script setup lang="ts">
/**
 * GenerationToast - small bottom-right notification for the in-flight adventure generation.
 * Mounted once in App.vue so it persists across navigation and never blocks the UI.
 */
import { watch, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAdventureGenerationStore } from '../store'

/** How long the success toast stays before dismissing itself (unless hovered). */
const AUTO_DISMISS_MS = 10_000

const store = useAdventureGenerationStore()
const route = useRoute()
const router = useRouter()

let timer: ReturnType<typeof setTimeout> | null = null

function stopTimer(): void {
  if (timer) clearTimeout(timer)
  timer = null
}

function startTimer(): void {
  stopTimer()
  if (store.status === 'completed' && store.toastVisible) {
    timer = setTimeout(() => store.dismissToast(), AUTO_DISMISS_MS)
  }
}

watch(
  () => store.status,
  (status) => {
    if (status !== 'completed') {
      stopTimer()
      return
    }
    // Still on the Adventure Forge: take the user straight to the result. Elsewhere, leave them be.
    if (route.name === 'adventure-generate' && store.adventureId) {
      const adventureId = store.adventureId
      store.dismissToast()
      void router.push({ name: 'adventure-review', params: { adventureId } })
      return
    }
    startTimer()
  },
)

onUnmounted(stopTimer)

function viewAdventure(): void {
  stopTimer()
  store.dismissToast()
}
</script>

<template>
  <div
    v-if="store.toastVisible"
    class="fixed bottom-4 right-4 z-50 w-80 rounded-lg border border-surface-700/50 bg-surface-850 p-4 shadow-xl"
    role="status"
    aria-live="polite"
    data-testid="generation-toast"
    :data-state="store.status"
    @mouseenter="stopTimer"
    @mouseleave="startTimer"
  >
    <!-- In progress -->
    <template v-if="store.status === 'generating'">
      <p class="text-sm font-semibold text-white">⚔ Generating your adventure...</p>
      <p class="mt-1 text-sm text-gray-300" data-testid="toast-message">{{ store.message }}</p>
      <div class="mt-2 flex items-center gap-2">
        <div
          class="h-2 flex-1 overflow-hidden rounded bg-gray-800"
          role="progressbar"
          :aria-valuenow="store.percentComplete"
          aria-valuemin="0"
          aria-valuemax="100"
        >
          <div
            class="h-full bg-aircane-500 transition-all duration-500"
            :style="{ width: store.percentComplete + '%' }"
            data-testid="toast-bar"
          />
        </div>
        <span class="w-10 text-right text-xs text-gray-400" data-testid="toast-percent">{{ store.percentComplete }}%</span>
      </div>
    </template>

    <!-- Success -->
    <template v-else-if="store.status === 'completed'">
      <p class="text-sm font-semibold text-green-300">✦ Your adventure is ready!</p>
      <p class="mt-1 truncate text-sm text-gray-300">{{ store.adventureTitle }}</p>
      <router-link
        v-if="store.adventureId"
        :to="{ name: 'adventure-review', params: { adventureId: store.adventureId } }"
        class="mt-2 inline-block text-sm text-aircane-300 underline hover:text-white"
        data-testid="toast-view-link"
        @click="viewAdventure"
      >
        View Adventure →
      </router-link>
    </template>

    <!-- Failure -->
    <template v-else-if="store.status === 'failed'">
      <p class="text-sm font-semibold text-red-300">✗ Adventure generation failed</p>
      <p v-if="store.error" class="mt-1 text-xs text-gray-400" data-testid="toast-error">{{ store.error }}</p>
      <div class="mt-2 flex items-center gap-3">
        <button
          type="button"
          class="rounded-md bg-aircane-600 px-3 py-1 text-sm font-semibold text-white hover:bg-aircane-500"
          data-testid="toast-retry"
          @click="store.retry()"
        >
          Try Again
        </button>
        <button type="button" class="text-sm text-gray-400 hover:text-white" @click="store.dismissToast()">
          Dismiss
        </button>
      </div>
    </template>
  </div>
</template>

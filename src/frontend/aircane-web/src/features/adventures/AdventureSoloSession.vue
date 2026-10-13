<script setup lang="ts">
/**
 * AdventureSoloSession - the AI DM runs one player through a generated adventure, scene by scene.
 * Route: /adventures/solo/:sessionId
 *
 * Left: narration feed + (below) the action input. Right: the current scene (objectives, exits,
 * NPCs, clues found). The right panel is driven by the run's state, which the store keeps current
 * from REST responses and the Adventure* SignalR events.
 */
import { ref, computed, watch, nextTick, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAdventureSessionStore } from './stores/adventureSessionStore'

const props = defineProps<{
  sessionId: string
}>()

const router = useRouter()
const store = useAdventureSessionStore()

const actionText = ref('')
const feedEl = ref<HTMLElement | null>(null)

const session = computed(() => store.currentSession)
const scene = computed(() => session.value?.currentScene ?? null)
const isInProgress = computed(() => session.value?.status === 'InProgress')
const isCompleted = computed(() => session.value?.status === 'Completed')
const isAbandoned = computed(() => session.value?.status === 'Abandoned')
const canSend = computed(
  () => isInProgress.value && !store.isLoading && actionText.value.trim().length > 0,
)

function scrollToBottom(): void {
  if (feedEl.value) feedEl.value.scrollTop = feedEl.value.scrollHeight
}

onMounted(async () => {
  try {
    // Loads the run and, when the feed is empty, the saved conversation behind a "Resuming" divider.
    await store.openSession(props.sessionId)
  } catch {
    return // store.error is shown in the banner
  }
  await nextTick()
  scrollToBottom() // land on the most recent exchange
  await store.connectHub()
})

onUnmounted(() => {
  void store.disconnectHub()
})

// Keep the newest narration in view.
watch(
  () => store.narrationHistory.length,
  async () => {
    await nextTick()
    scrollToBottom()
  },
)

async function handleSend(): Promise<void> {
  if (!canSend.value) return
  const text = actionText.value
  actionText.value = ''
  try {
    await store.sendPlayerAction(text)
  } catch {
    actionText.value = text // let the player retry without retyping
  }
}

async function handleExit(sceneId: string): Promise<void> {
  try {
    await store.advanceScene(sceneId)
  } catch {
    // store.error is shown in the banner
  }
}

async function handleAbandon(): Promise<void> {
  if (!confirm('Abandon this adventure? Your progress will be lost.')) return
  try {
    await store.abandonSession()
    store.clearSession()
    await router.push('/campaigns')
  } catch {
    // store.error is shown in the banner
  }
}

function npcStatus(npc: { hasBeenMet: boolean; isAlive: boolean }): string {
  if (!npc.isAlive) return 'dead'
  return npc.hasBeenMet ? 'met' : 'alive'
}
</script>

<template>
  <div class="mx-auto flex max-w-6xl flex-col gap-4" data-testid="adventure-solo-session">
    <!-- Header -->
    <header class="flex items-center justify-between gap-4">
      <div class="min-w-0">
        <h1 class="truncate text-2xl font-bold text-white" data-testid="adventure-title">
          {{ session?.adventureTitle ?? 'Loading adventure…' }}
        </h1>
        <p v-if="session" class="text-sm text-gray-400" data-testid="scene-progress">
          Scene {{ session.currentSceneNumber }} of {{ session.totalScenes }}
        </p>
      </div>
      <button
        v-if="isInProgress"
        type="button"
        data-testid="abandon-button"
        class="rounded-md border border-red-800 bg-red-950 px-3 py-1.5 text-sm font-medium text-red-300 transition-colors hover:bg-red-900 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
        :disabled="store.isLoading"
        @click="handleAbandon"
      >
        Abandon
      </button>
    </header>

    <!-- Errors -->
    <div
      v-if="store.error"
      role="alert"
      class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
      data-testid="error-banner"
    >
      {{ store.error }}
    </div>

    <!-- Finished banners -->
    <output
      v-if="isCompleted"
      class="block rounded-lg border border-green-800 bg-green-950 px-4 py-3 text-sm text-green-300"
      data-testid="completed-banner"
    >
      <p class="font-medium">Adventure complete!</p>
      <p v-if="session?.outcomeNotes" class="mt-1">{{ session.outcomeNotes }}</p>
    </output>
    <output
      v-else-if="isAbandoned"
      class="block rounded-lg border border-gray-700 bg-gray-900 px-4 py-3 text-sm text-gray-300"
      data-testid="abandoned-banner"
    >
      This adventure was abandoned.
    </output>

    <div v-if="!session && store.isLoading" class="py-20 text-center text-gray-400" aria-busy="true">
      Loading adventure…
    </div>

    <template v-if="session && scene">
      <div class="grid grid-cols-1 gap-4 lg:grid-cols-3">
        <!-- Narration feed -->
        <section
          class="flex flex-col rounded-xl border border-surface-700/50 bg-surface-850 lg:col-span-2"
          style="height: 28rem"
          aria-label="Narration"
        >
          <div
            ref="feedEl"
            role="log"
            aria-live="polite"
            aria-label="Narration feed"
            class="flex-1 space-y-3 overflow-y-auto p-5"
            data-testid="narration-feed"
          >
            <!-- "Story so far": an AI-written reminder, above the saved conversation -->
            <aside
              v-if="store.recap"
              class="rounded-lg border border-amber-600/60 bg-gray-950 px-4 py-3"
              data-testid="recap-block"
              aria-label="Story so far"
            >
              <p class="text-sm font-semibold text-amber-300">📖 Story So Far</p>
              <p class="mt-1 text-sm italic leading-relaxed text-gray-300" data-testid="recap-text">{{ store.recap }}</p>
            </aside>
            <p v-else-if="store.recapLoading" class="text-center text-xs italic text-gray-500" data-testid="recap-loading">
              Recalling the story so far…
            </p>
            <p
              v-for="entry in store.narrationHistory"
              :key="entry.id"
              :data-testid="entry.kind === 'separator' ? 'resume-separator' : 'narration-entry'"
              :data-kind="entry.kind"
              class="whitespace-pre-wrap text-sm leading-relaxed"
              :class="{
                'text-gray-100': entry.kind === 'dm',
                'text-right italic text-aircane-300': entry.kind === 'player',
                'text-center text-xs uppercase tracking-wide text-gray-500': entry.kind === 'system',
                'text-center text-xs tracking-wide text-gray-600': entry.kind === 'separator',
              }"
            >{{ entry.text }}</p>
            <p v-if="store.isLoading" class="text-sm italic text-gray-500" aria-live="polite">
              The DM is thinking…
            </p>
          </div>
        </section>

        <!-- Current scene -->
        <aside
          class="space-y-5 rounded-xl border border-surface-700/50 bg-surface-850 p-5"
          aria-label="Current scene"
        >
          <div>
            <h2 class="text-xs font-medium uppercase tracking-wider text-gray-400">Current scene</h2>
            <p class="mt-1 text-lg font-semibold text-white" data-testid="scene-title">{{ scene.title }}</p>
          </div>

          <div v-if="scene.objectives.length">
            <h3 class="text-xs font-medium uppercase tracking-wider text-gray-400">Objectives</h3>
            <ul class="mt-1 space-y-1 text-sm text-gray-200">
              <li v-for="objective in scene.objectives" :key="objective" data-testid="objective">
                • {{ objective }}
              </li>
            </ul>
          </div>

          <div>
            <h3 class="text-xs font-medium uppercase tracking-wider text-gray-400">Exits</h3>
            <p v-if="!scene.exits.length" class="mt-1 text-sm text-gray-500">No exits from here.</p>
            <ul v-else class="mt-1 space-y-1">
              <li v-for="exit in scene.exits" :key="exit.sceneId">
                <button
                  type="button"
                  data-testid="exit-button"
                  class="w-full rounded-md px-2 py-1 text-left text-sm text-aircane-300 transition-colors hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
                  :disabled="!isInProgress || store.isLoading"
                  @click="handleExit(exit.sceneId)"
                >
                  → {{ exit.title }}
                </button>
              </li>
            </ul>
          </div>

          <div>
            <h3 class="text-xs font-medium uppercase tracking-wider text-gray-400">NPCs here</h3>
            <p v-if="!scene.npcs.length" class="mt-1 text-sm text-gray-500">Nobody in sight.</p>
            <ul v-else class="mt-1 space-y-1 text-sm">
              <li v-for="npc in scene.npcs" :key="npc.npcId" data-testid="npc-row" class="text-gray-200">
                • {{ npc.npcName }}
                <span
                  data-testid="npc-status"
                  :class="npc.isAlive ? 'text-gray-400' : 'text-red-400'"
                >({{ npcStatus(npc) }})</span>
              </li>
            </ul>
          </div>

          <div>
            <h3 class="text-xs font-medium uppercase tracking-wider text-gray-400">Clues found</h3>
            <p v-if="!session.revealedClues.length && !session.revealedSecrets.length" class="mt-1 text-sm text-gray-500">
              Nothing yet.
            </p>
            <ul v-else class="mt-1 space-y-1 text-sm text-gray-200">
              <li v-for="clue in session.revealedClues" :key="clue.id" data-testid="clue-item" :title="clue.description">
                • {{ clue.name }}
              </li>
              <li
                v-for="secret in session.revealedSecrets"
                :key="secret.id"
                data-testid="secret-item"
                class="text-purple-300"
                :title="secret.description"
              >
                • {{ secret.name }}
              </li>
            </ul>
          </div>
        </aside>
      </div>

      <!-- Action input -->
      <form class="flex items-center gap-3" data-testid="action-form" @submit.prevent="handleSend">
        <label for="adventure-action" class="sr-only">What do you do?</label>
        <span class="text-gray-500" aria-hidden="true">&gt;</span>
        <input
          id="adventure-action"
          v-model="actionText"
          type="text"
          data-testid="action-input"
          autocomplete="off"
          class="flex-1 rounded-md border border-surface-700/50 bg-surface-850 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-50"
          :placeholder="isInProgress ? 'What do I do?' : 'This adventure has ended.'"
          :disabled="!isInProgress || store.isLoading"
        />
        <button
          type="submit"
          data-testid="send-button"
          class="rounded-md bg-aircane-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!canSend"
        >
          Send
        </button>
      </form>
    </template>
  </div>
</template>

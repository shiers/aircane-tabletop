<script setup lang="ts">
/**
 * GroupRunHostPanel - the host's view of a group adventure that is under way.
 *
 * Shows the current scene, whose turn it is, the round and the turn order, with the controls only
 * the host has: pass the turn, move the scene and abandon. Bringing in more players (join link, invite
 * code, approving, adding to the turn order) lives in the InvitePlayersPanel, which the parent supplies
 * through the `invite` slot.
 *
 * The host takes no turn. Players act one at a time; the server does not pass the turn on its own,
 * so a player who has disconnected, or who is simply slow, keeps the turn until the host passes it.
 * That is why "Pass the turn" is the most prominent control on the screen. When the parent knows who is
 * connected (`presence`), a disconnected acting player is pointed out there, with a way to remove them.
 * When everyone has left the turn order there is no acting player until the host adds one.
 */
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useGroupRunStore } from '../stores/groupRunStore'

const props = defineProps<{
  /** Participant id -> connected right now, from the host's roster. Missing ids are unknown. */
  presence?: Record<string, boolean | null | undefined>
}>()

const emit = defineEmits<{
  (e: 'abandoned'): void
  /** Take a participant out of the session (the parent confirms it). */
  (e: 'remove', participantId: string): void
}>()

const store = useGroupRunStore()
const { run, busy, error, notice, nextParticipantName, nextStartsNewRound } = storeToRefs(store)

/**
 * Where "Advance scene" will move the party. This picks a DESTINATION: the scenes reachable from the
 * current one (the server rejects any other, including the current scene itself), so it never shows the
 * current scene. Nothing is preselected, so the host cannot move the party by accident.
 */
const targetSceneId = ref('')

const exits = computed(() => run.value?.currentScene.exits ?? [])
const finished = computed(() => !!run.value && run.value.status !== 'InProgress')
const finishedLabel = computed(() => {
  switch (run.value?.status) {
    case 'Completed':
      return 'The adventure is complete.'
    case 'Abandoned':
      return 'The adventure was abandoned.'
    default:
      return null
  }
})

// A chosen destination is forgotten once the scene changes and it is no longer one of the exits.
watch(
  exits,
  (list) => {
    if (!list.some((e) => e.sceneId === targetSceneId.value)) targetSceneId.value = ''
  },
  { immediate: true },
)

/** Everyone has left the turn order: nobody can act until the host adds a player. */
const noActingPlayer = computed(() => !!run.value && run.value.turnOrder.length === 0)

/** The acting player, when the roster says they have no connection open. */
const actingAway = computed(() => {
  const acting = run.value?.turnOrder.find((t) => t.isActive)
  return acting && props.presence?.[acting.participantId] === false ? acting : null
})

function presenceOf(participantId: string): 'Connected' | 'Away' | null {
  const value = props.presence?.[participantId]
  return value === true ? 'Connected' : value === false ? 'Away' : null
}

const passLabel = computed(() => {
  const next = nextParticipantName.value
  if (!next) return 'Pass the turn'
  if ((run.value?.turnOrder.length ?? 0) === 1) return 'Start the next round'
  return `Pass the turn to ${next}`
})

async function handleAdvanceScene(): Promise<void> {
  if (targetSceneId.value) await store.advanceScene(targetSceneId.value)
}

async function handleAbandon(): Promise<void> {
  if (!confirm('Abandon this adventure for everyone? Players will lose their place in it.')) return
  if (await store.abandon()) emit('abandoned')
}
</script>

<template>
  <section v-if="run" class="space-y-5" aria-labelledby="group-run-heading" data-testid="group-run-panel">
    <div class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
      <h2 id="group-run-heading" class="text-lg font-semibold text-white">{{ run.adventureTitle }}</h2>
      <p class="text-sm text-gray-400" data-testid="run-progress">
        Scene {{ run.currentSceneNumber }} of {{ run.totalScenes }} · Round {{ run.currentRound }}
      </p>
    </div>

    <div v-if="error" role="alert" class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300" data-testid="run-error">
      {{ error }}
    </div>
    <p v-if="notice" role="status" class="rounded-lg border border-aircane-700 bg-aircane-900/40 px-4 py-2 text-sm text-aircane-200" data-testid="run-notice">
      {{ notice }}
    </p>

    <p v-if="finished" class="rounded-lg border border-surface-700/50 bg-surface-900 px-4 py-3 text-sm text-gray-200" data-testid="run-finished">
      {{ finishedLabel }}
      <span v-if="run.outcomeNotes" class="block text-gray-400">{{ run.outcomeNotes }}</span>
    </p>

    <template v-else>
      <!-- Whose turn it is, and the one control the host will use most -->
      <div class="rounded-xl border-2 border-aircane-600 bg-aircane-900/30 p-4 sm:p-5" data-testid="turn-card">
        <p class="text-xs uppercase tracking-wider text-aircane-300">Acting now</p>
        <p v-if="noActingPlayer" class="mt-1 break-words text-lg font-semibold text-amber-200" data-testid="no-acting-player">
          No acting player. Approve and add a player to continue.
        </p>
        <p v-else class="mt-1 break-words text-2xl font-bold text-white" data-testid="active-player">
          {{ run.activeParticipantName ?? 'Nobody yet' }}
        </p>

        <button
          type="button"
          data-testid="advance-turn-button"
          class="mt-4 w-full rounded-lg bg-aircane-600 px-4 py-4 text-base font-bold text-white shadow-lg hover:bg-aircane-500 focus:outline-none focus:ring-4 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 sm:w-auto sm:px-8"
          :disabled="busy !== null || run.turnOrder.length === 0"
          @click="store.advanceTurn()"
        >
          {{ busy === 'turn' ? 'Passing the turn…' : `⏭ ${passLabel}` }}
        </button>
        <div
          v-if="actingAway"
          class="mt-3 flex flex-wrap items-center gap-2 rounded-md border border-amber-700 bg-amber-950/40 px-3 py-2 text-sm text-amber-200"
          role="status"
          data-testid="acting-player-away"
        >
          <span class="min-w-0 break-words">{{ actingAway.participantName }} is disconnected. Pass the turn, or remove them.</span>
          <button
            type="button"
            class="shrink-0 rounded-md border border-red-800 px-3 py-1 text-sm font-medium text-red-300 hover:bg-red-950 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
            :disabled="busy !== null"
            data-testid="remove-acting-player"
            @click="emit('remove', actingAway.participantId)"
          >
            Remove {{ actingAway.participantName }}
          </button>
        </div>
        <p v-if="nextStartsNewRound && run.turnOrder.length > 1" class="mt-2 text-xs text-aircane-200" data-testid="new-round-hint">
          That starts round {{ run.currentRound + 1 }}.
        </p>
        <p class="mt-2 text-sm text-gray-300" data-testid="turn-hint">
          The turn only moves when you pass it. If the acting player has disconnected or stepped away, they keep the turn until you do.
        </p>
      </div>

      <!-- Invite players: join link, QR, invite code, approvals (supplied by the host page) -->
      <slot name="invite" />

      <!-- Turn order -->
      <div>
        <h3 class="mb-2 text-sm font-medium uppercase tracking-wider text-gray-400">Turn order</h3>
        <ol class="space-y-1" data-testid="run-turn-order">
          <li
            v-for="(entry, index) in run.turnOrder"
            :key="entry.participantId"
            class="flex items-center gap-3 rounded-md border px-3 py-2 text-sm"
            :class="entry.isActive ? 'border-aircane-500 bg-aircane-900/40 text-white' : 'border-surface-700/50 bg-surface-900 text-gray-300'"
            :aria-current="entry.isActive ? 'step' : undefined"
          >
            <span class="w-5 shrink-0 text-right text-gray-500">{{ index + 1 }}</span>
            <span
              v-if="presenceOf(entry.participantId)"
              class="h-2.5 w-2.5 shrink-0 rounded-full"
              :class="presenceOf(entry.participantId) === 'Connected' ? 'bg-green-400' : 'bg-gray-500'"
              role="img"
              :aria-label="presenceOf(entry.participantId)!"
              :title="presenceOf(entry.participantId)!"
              data-testid="turn-presence-dot"
            />
            <span class="min-w-0 flex-1 truncate">{{ entry.participantName }}</span>
            <span v-if="entry.isActive" class="shrink-0 text-xs font-semibold text-aircane-300">acting</span>
            <button
              type="button"
              class="shrink-0 rounded px-2 py-0.5 text-xs text-red-300 hover:bg-red-950 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
              :disabled="busy !== null"
              :aria-label="`Remove ${entry.participantName} from the session`"
              data-testid="remove-turn-player"
              @click="emit('remove', entry.participantId)"
            >
              Remove
            </button>
          </li>
        </ol>
        <p v-if="noActingPlayer" class="text-sm text-gray-400" data-testid="turn-order-empty">
          Nobody is in the turn order.
        </p>
        <p class="mt-2 text-xs text-gray-500" data-testid="host-seat-hint">
          You run the game as the host and don't take a turn. Want a seat? Join as a player from another browser with the invite code.
        </p>
      </div>

      <!-- Host scene override -->
      <div v-if="exits.length" class="space-y-2" data-testid="scene-controls">
        <h3 class="text-sm font-medium uppercase tracking-wider text-gray-400" data-testid="current-scene">
          Current scene: {{ run.currentScene.title }}
        </h3>
        <label for="advance-scene-target" class="block text-sm text-gray-300">Move the party to…</label>
        <div class="flex flex-col gap-2 sm:flex-row">
          <select
            id="advance-scene-target"
            v-model="targetSceneId"
            data-testid="scene-select"
            class="min-w-0 flex-1 rounded-md border border-surface-700/50 bg-surface-900 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          >
            <option value="" disabled>Choose a scene…</option>
            <option v-for="exit in exits" :key="exit.sceneId" :value="exit.sceneId">{{ exit.title }}</option>
          </select>
          <button
            type="button"
            data-testid="advance-scene-button"
            class="rounded-md border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-100 hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
            :disabled="busy !== null || !targetSceneId"
            @click="handleAdvanceScene"
          >
            {{ busy === 'scene' ? 'Moving…' : 'Advance scene' }}
          </button>
        </div>
        <p class="text-xs text-gray-500" data-testid="scene-hint">The AI GM moves the story along too; use this to override it.</p>
      </div>
      <p v-else class="text-sm text-gray-400" data-testid="no-exits">
        Current scene: {{ run.currentScene.title }}. It has no exits to move on to.
      </p>

      <div class="border-t border-surface-700/50 pt-4">
        <button
          type="button"
          data-testid="abandon-button"
          class="rounded-md border border-red-800 bg-red-950 px-3 py-1.5 text-sm font-medium text-red-300 hover:bg-red-900 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
          :disabled="busy !== null"
          @click="handleAbandon"
        >
          {{ busy === 'abandon' ? 'Abandoning…' : 'Abandon adventure' }}
        </button>
      </div>
    </template>
  </section>
</template>

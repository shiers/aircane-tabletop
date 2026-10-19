<script setup lang="ts">
/**
 * WaitingForApproval - the screen a player sees between joining and being let in.
 *
 * Listens for the host's decision (hub events, with a poll as the fallback; see approvalWatch.ts) and
 * tells its parent: `approved` to move on to the session, `denied` is shown here and stops, and
 * `rejected` means the stored login is no longer accepted, so the parent offers a fresh join.
 */
import { watch, onMounted, onUnmounted, computed } from 'vue'
import { useApprovalWatch, type ApprovalWatchOptions } from '../approvalWatch'
import { DENIED_MESSAGE } from '../playerErrors'
import { onPageVisible } from '../visibility'

const props = defineProps<{
  sessionId: string
  participantId: string
  displayName: string
  /** Test seam: replaces the status read and the live connection. */
  watchOptions?: Partial<ApprovalWatchOptions>
}>()

const emit = defineEmits<{
  approved: []
  rejected: []
  /** The player wants to try again after a denial. */
  rejoin: []
}>()

const { state, hubConnected, connectionProblem, start, stop, refresh } = useApprovalWatch({
  sessionId: props.sessionId,
  me: { participantId: props.participantId, displayName: props.displayName },
  ...props.watchOptions,
})

watch(state, (value) => {
  if (value === 'approved') emit('approved')
  else if (value === 'rejected') emit('rejected')
})

// Coming back to the page (another app, a locked phone) means events may have been missed: look now.
let stopWatchingVisibility: () => void = () => undefined

onMounted(() => {
  start()
  stopWatchingVisibility = onPageVisible(refresh)
})
onUnmounted(() => {
  stopWatchingVisibility()
  stop()
})

const isDenied = computed(() => state.value === 'denied')
</script>

<template>
  <div
    class="rounded-xl border border-surface-700/50 bg-surface-850 p-6 text-center sm:p-8"
    aria-live="polite"
    data-testid="waiting-for-approval"
  >
    <!-- Denied: say so clearly and stop -->
    <template v-if="isDenied">
      <div class="mb-4 text-4xl" aria-hidden="true">🚪</div>
      <h2 class="mb-2 text-xl font-semibold text-white">Join request declined</h2>
      <p role="alert" class="text-sm text-red-300" data-testid="denied-message">{{ DENIED_MESSAGE }}</p>
      <button
        type="button"
        class="mt-6 min-h-[44px] rounded-lg border border-gray-600 px-5 py-2 text-sm font-medium text-gray-200 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="emit('rejoin')"
      >
        Join again
      </button>
    </template>

    <!-- Waiting (or checking for the first time) -->
    <template v-else>
      <div class="mb-4 text-4xl" aria-hidden="true">🎲</div>
      <h2 class="mb-2 text-xl font-semibold text-white">Welcome, {{ displayName }}!</h2>
      <p class="text-sm text-yellow-400" data-testid="pending-message">
        <template v-if="state === 'checking'">Checking your join request…</template>
        <template v-else>Your join request is pending host approval. You will be let in as soon as the host approves you.</template>
      </p>
      <p v-if="connectionProblem" role="status" class="mt-3 text-sm text-orange-300" data-testid="connection-problem">
        Can't reach the host right now. Still trying…
      </p>
      <p class="mt-4 text-xs text-gray-500">
        {{ hubConnected ? 'Connected to the table.' : 'Checking with the host every few seconds.' }}
        Keep this page open.
      </p>
    </template>
  </div>
</template>

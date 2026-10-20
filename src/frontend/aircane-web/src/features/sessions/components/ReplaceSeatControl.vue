<script setup lang="ts">
/**
 * ReplaceSeatControl - on a waiting join request whose name matches someone already in the session (the server
 * flags it with `sameNameAsParticipantId`): an "Already in this session" badge and, when that participant has a
 * seat, a "Replace seat" action. It names the participant it would replace and asks for confirmation; the new
 * request then takes their character and place in the turn order, and the old login is signed out. Approve stays
 * next to it, for two different people who happen to share a name.
 */
import { computed, ref } from 'vue'
import type { ParticipantDto } from '../api'

const props = defineProps<{
  request: ParticipantDto
  /** Everyone on the host's roster, to find the participant the request matches. */
  roster: readonly ParticipantDto[]
  busy?: boolean
}>()

const emit = defineEmits<{
  /** Give `oldParticipantId`'s seat to this request. */
  (e: 'replace', newParticipantId: string, oldParticipantId: string): void
}>()

const existing = computed(() =>
  props.request.sameNameAsParticipantId
    ? props.roster.find((p) => p.id === props.request.sameNameAsParticipantId) ?? null
    : null,
)

/** Only a seat can be handed over: the match must be approved (the server refuses otherwise). */
const canReplace = computed(() => !!existing.value?.isApproved)

const confirming = ref(false)

function confirm(): void {
  if (!existing.value) return
  confirming.value = false
  emit('replace', props.request.id, existing.value.id)
}
</script>

<template>
  <div v-if="request.sameNameAsParticipantId" class="mt-1 min-w-0 space-y-2" data-testid="same-name">
    <p class="flex flex-wrap items-center gap-2 text-xs">
      <span
        class="rounded-full border border-amber-600 bg-amber-950/50 px-2 py-0.5 font-medium text-amber-200"
        data-testid="same-name-badge"
      >
        Already in this session
      </span>
      <span class="text-gray-400">
        A participant named {{ existing?.displayName ?? request.displayName }} is already here.
      </span>
    </p>

    <button
      v-if="canReplace && !confirming"
      type="button"
      class="min-h-[36px] rounded-md border border-amber-600 px-3 py-1.5 text-sm font-medium text-amber-200 hover:bg-amber-900/40 focus:outline-none focus:ring-2 focus:ring-amber-500 disabled:opacity-50"
      :disabled="busy"
      :aria-label="`Replace ${existing?.displayName}'s seat with this request`"
      data-testid="replace-seat"
      @click="confirming = true"
    >
      Replace seat
    </button>

    <div
      v-if="confirming && existing"
      role="alertdialog"
      :aria-label="`Replace ${existing.displayName}'s seat?`"
      class="rounded-md border border-amber-700 bg-amber-950/40 p-3 text-sm text-amber-100"
      data-testid="replace-seat-confirm"
    >
      <p>
        Give <strong data-testid="replace-seat-target">{{ existing.displayName }}</strong>'s seat to this request?
        It takes their character and their place in the turn order, and the turn if it is theirs.
        The {{ existing.displayName }} already at the table is signed out.
      </p>
      <div class="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          class="min-h-[36px] rounded-md bg-amber-700 px-3 py-1.5 font-medium text-white hover:bg-amber-600 focus:outline-none focus:ring-2 focus:ring-amber-400 disabled:opacity-50"
          :disabled="busy"
          data-testid="confirm-replace"
          @click="confirm"
        >
          Replace seat
        </button>
        <button
          type="button"
          class="min-h-[36px] rounded-md border border-gray-600 px-3 py-1.5 text-gray-200 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-gray-500"
          data-testid="cancel-replace"
          @click="confirming = false"
        >
          Cancel
        </button>
      </div>
    </div>
  </div>
</template>

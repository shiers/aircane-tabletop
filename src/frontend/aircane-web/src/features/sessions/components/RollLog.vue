<script setup lang="ts">
/**
 * RollLog - the table's roll log, newest first, for the host and player screens.
 * One line per roll: who rolled, the formula, the dice, the total, and when. A private roll (visible only
 * to its roller and the host) is marked as such.
 */
import { RollVisibility, type RollDto } from '../../dice/api'
import { describeRoll } from '../rollLog'

defineProps<{
  rolls: RollDto[]
  /** The viewer's participant id, to call their own rolls "You". Null for the host. */
  selfParticipantId?: string | null
  /** Why the log could not be loaded, if it could not. */
  error?: string | null
}>()

function formatRollTime(timestamp: string): string {
  try {
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}
</script>

<template>
  <div
    role="log"
    aria-label="Roll log"
    aria-live="polite"
    class="max-h-64 space-y-1 overflow-y-auto"
  >
    <p v-if="error" role="status" class="py-1 text-center text-xs text-orange-300" data-testid="roll-log-error">{{ error }}</p>
    <p v-if="rolls.length === 0" class="py-4 text-center text-xs text-gray-600">
      No rolls yet.
    </p>
    <div
      v-for="roll in rolls"
      :key="roll.id"
      class="flex items-baseline justify-between gap-2 rounded-md px-2 py-1 text-sm hover:bg-gray-800"
      data-testid="roll-entry"
      :data-visibility="roll.visibility === RollVisibility.Private ? 'private' : 'public'"
    >
      <span class="min-w-0 break-words text-gray-300">
        {{ describeRoll(roll, selfParticipantId ?? null) }}
        <span
          v-if="roll.visibility === RollVisibility.Private"
          class="ml-1 rounded bg-surface-700 px-1 text-xs text-gray-400"
          title="Only you and the host see this roll"
        >private</span>
      </span>
      <time :datetime="roll.createdAt" class="shrink-0 text-xs text-gray-600">
        {{ formatRollTime(roll.createdAt) }}
      </time>
    </div>
  </div>
</template>

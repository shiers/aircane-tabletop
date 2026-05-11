<script setup lang="ts">
/**
 * ParticipantApprovalPanel — shown to the host during a session.
 * Lists all participants, highlights pending approvals, and lets the host
 * approve players and assign characters.
 */
import { computed } from 'vue'
import { ParticipantRole, type ParticipantDto } from './api'

// ---------------------------------------------------------------------------
// Props & Emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  sessionId: string
  participants: ParticipantDto[]
  /** Characters available in the campaign for assignment. */
  availableCharacters: { id: string; name: string }[]
  loading?: boolean
}>()

const emit = defineEmits<{
  (e: 'approve', participantId: string): void
  (e: 'assign-character', participantId: string, characterId: string): void
}>()

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

const pendingParticipants = computed(() =>
  props.participants.filter((p) => !p.isApproved),
)

const approvedParticipants = computed(() =>
  props.participants.filter((p) => p.isApproved),
)

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function roleLabel(role: ParticipantRole): string {
  const map: Record<ParticipantRole, string> = {
    [ParticipantRole.Player]: 'Player',
    [ParticipantRole.HumanDm]: 'Human DM',
    [ParticipantRole.Host]: 'Host',
  }
  return map[role] ?? 'Unknown'
}

function characterName(characterId: string | null): string {
  if (!characterId) return '—'
  return props.availableCharacters.find((c) => c.id === characterId)?.name ?? 'Unknown'
}

function onApprove(participantId: string): void {
  emit('approve', participantId)
}

function onAssignCharacter(participantId: string, event: Event): void {
  const select = event.target as HTMLSelectElement
  if (select.value) {
    emit('assign-character', participantId, select.value)
  }
}
</script>

<template>
  <section aria-labelledby="approval-panel-heading" class="space-y-6">
    <h2 id="approval-panel-heading" class="text-lg font-semibold text-white">
      Participants
    </h2>

    <!-- Pending approvals -->
    <div v-if="pendingParticipants.length > 0">
      <h3 class="mb-3 text-sm font-medium uppercase tracking-wider text-yellow-400">
        Awaiting Approval ({{ pendingParticipants.length }})
      </h3>
      <ul class="space-y-2" aria-label="Pending participants">
        <li
          v-for="participant in pendingParticipants"
          :key="participant.id"
          class="flex items-center justify-between gap-4 rounded-lg border border-yellow-800 bg-yellow-950/30 px-4 py-3"
        >
          <div>
            <p class="font-medium text-white">{{ participant.displayName }}</p>
            <p class="text-xs text-gray-400">{{ roleLabel(participant.role) }}</p>
          </div>
          <button
            type="button"
            :disabled="loading"
            class="rounded-md bg-green-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-600 focus:outline-none focus:ring-2 focus:ring-green-500 disabled:opacity-50 transition-colors"
            :aria-label="`Approve ${participant.displayName}`"
            @click="onApprove(participant.id)"
          >
            Approve
          </button>
        </li>
      </ul>
    </div>

    <p
      v-else-if="participants.length === 0"
      class="text-sm text-gray-500"
    >
      No participants have joined yet.
    </p>

    <!-- Approved participants -->
    <div v-if="approvedParticipants.length > 0">
      <h3 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
        Approved ({{ approvedParticipants.length }})
      </h3>
      <ul class="space-y-2" aria-label="Approved participants">
        <li
          v-for="participant in approvedParticipants"
          :key="participant.id"
          class="flex flex-col gap-3 rounded-lg border border-gray-700 bg-gray-800 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <div class="min-w-0">
            <p class="font-medium text-white">{{ participant.displayName }}</p>
            <p class="text-xs text-gray-400">{{ roleLabel(participant.role) }}</p>
          </div>

          <!-- Character assignment -->
          <div class="flex items-center gap-2">
            <span class="text-xs text-gray-500">Character:</span>
            <span
              v-if="availableCharacters.length === 0"
              class="text-sm text-gray-400"
            >
              {{ characterName(participant.characterId) }}
            </span>
            <select
              v-else
              :id="`character-select-${participant.id}`"
              :value="participant.characterId ?? ''"
              :disabled="loading"
              class="rounded-md border border-gray-600 bg-gray-700 px-2 py-1 text-sm text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50"
              :aria-label="`Assign character to ${participant.displayName}`"
              @change="onAssignCharacter(participant.id, $event)"
            >
              <option value="" disabled>Select character…</option>
              <option
                v-for="character in availableCharacters"
                :key="character.id"
                :value="character.id"
              >
                {{ character.name }}
              </option>
            </select>
          </div>
        </li>
      </ul>
    </div>
  </section>
</template>

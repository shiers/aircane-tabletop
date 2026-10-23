<script setup lang="ts">
/**
 * ParticipantApprovalPanel - shown to the host during a session.
 * Lists all participants, highlights pending approvals, and lets the host approve or deny a request, remove an
 * approved player, and assign characters. Each person shows whether they are connected right now (when the
 * roster says; it is the host's read only). Denying and removing are asked to the parent, which confirms them.
 */
import { computed } from 'vue'
import { ParticipantRole, type ParticipantDto } from './api'
import ReplaceSeatControl from './components/ReplaceSeatControl.vue'
import { features } from '@/shared/features'

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
  /** Give an approved participant's seat to a waiting request with the same name. */
  (e: 'replace', newParticipantId: string, oldParticipantId: string): void
  /** Decline a waiting request (the server tells the person and revokes their login). */
  (e: 'deny', participantId: string): void
  /** Take an approved participant out of the session. */
  (e: 'remove', participantId: string): void
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
    [ParticipantRole.Spectator]: 'Spectator',
  }
  return map[role] ?? 'Unknown'
}

function characterName(characterId: string | null): string {
  if (!characterId) return '-'
  return props.availableCharacters.find((c) => c.id === characterId)?.name ?? 'Unknown'
}

/** "Connected" / "Away", or null when the roster does not say (a player's read). */
function presenceLabel(p: ParticipantDto): string | null {
  if (p.isConnected === true) return 'Connected'
  if (p.isConnected === false) return 'Away'
  return null
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
          class="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-yellow-800 bg-yellow-950/30 px-4 py-3"
        >
          <div class="min-w-0">
            <p class="flex min-w-0 items-center gap-2 font-medium text-white">
              <span
                v-if="presenceLabel(participant)"
                class="h-2.5 w-2.5 shrink-0 rounded-full"
                :class="participant.isConnected ? 'bg-green-400' : 'bg-gray-500'"
                role="img"
                :aria-label="presenceLabel(participant)!"
                :title="presenceLabel(participant)!"
                data-testid="presence-dot"
              />
              <span class="min-w-0 break-words">{{ participant.displayName }}</span>
            </p>
            <p class="text-xs text-gray-400">{{ roleLabel(participant.role) }}</p>
            <ReplaceSeatControl
              :request="participant"
              :roster="participants"
              :busy="loading"
              @replace="(newId: string, oldId: string) => emit('replace', newId, oldId)"
            />
          </div>
          <div class="flex shrink-0 items-center gap-2">
            <button
              type="button"
              :disabled="loading"
              class="rounded-md border border-red-800 px-3 py-1.5 text-sm font-medium text-red-300 hover:bg-red-950 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50 transition-colors"
              :aria-label="`Deny ${participant.displayName}`"
              title="Declines the request. They are told, and cannot take part unless they ask again."
              data-testid="deny-participant"
              @click="emit('deny', participant.id)"
            >
              Deny
            </button>
            <button
              type="button"
              :disabled="loading"
              class="rounded-md bg-green-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-600 focus:outline-none focus:ring-2 focus:ring-green-500 disabled:opacity-50 transition-colors"
              :aria-label="`Approve ${participant.displayName}`"
              @click="onApprove(participant.id)"
            >
              Approve
            </button>
          </div>
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
            <p class="flex min-w-0 items-center gap-2 font-medium text-white">
              <span
                v-if="presenceLabel(participant)"
                class="h-2.5 w-2.5 shrink-0 rounded-full"
                :class="participant.isConnected ? 'bg-green-400' : 'bg-gray-500'"
                role="img"
                :aria-label="presenceLabel(participant)!"
                :title="presenceLabel(participant)!"
                data-testid="presence-dot"
              />
              <span class="min-w-0 break-words">{{ participant.displayName }}</span>
            </p>
            <p class="text-xs text-gray-400">
              {{ roleLabel(participant.role) }}<template v-if="presenceLabel(participant)"> · {{ presenceLabel(participant) }}</template>
            </p>
          </div>

          <!-- Character assignment (only while the Characters feature is on), and removal -->
          <div class="flex flex-wrap items-center gap-2">
            <template v-if="features.characters">
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
            </template>
            <button
              v-if="participant.role !== ParticipantRole.Host"
              type="button"
              :disabled="loading"
              class="rounded-md border border-red-800 px-3 py-1 text-sm font-medium text-red-300 hover:bg-red-950 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
              :aria-label="`Remove ${participant.displayName} from the session`"
              data-testid="remove-participant"
              @click="emit('remove', participant.id)"
            >
              Remove
            </button>
          </div>
        </li>
      </ul>
    </div>
  </section>
</template>

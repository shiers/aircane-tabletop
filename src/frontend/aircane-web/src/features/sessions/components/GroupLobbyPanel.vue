<script setup lang="ts">
/**
 * GroupLobbyPanel - the host's lobby for a group adventure.
 *
 * Players join with the invite code and wait here. The host approves them, assigns characters,
 * orders the turns, and starts the adventure. Everything the lobby decides is a pure function in
 * `../lobby` (who can take a turn, the order, when Start is enabled) so it can be tested alone.
 *
 * The server never tells the host that someone joined or was approved by REST, so the parent keeps
 * `participants` fresh by polling; this panel only reacts to the list it is given.
 */
import { ref, computed, watch } from 'vue'
import { SessionStatus, type ParticipantDto, type SessionDto } from '../api'
import {
  eligiblePlayers,
  pendingParticipants,
  reconcileTurnOrder,
  moveInTurnOrder,
  startDisabledReason,
  type JoinLink,
} from '../lobby'
import { startGroupAdventureSession, type AdventureSessionStateDto } from '@/features/adventures/api'
import { mapGroupError } from '@/features/adventures/groupErrors'
import LanJoinScreen from '../LanJoinScreen.vue'
import ParticipantApprovalPanel from '../ParticipantApprovalPanel.vue'

const props = defineProps<{
  sessionId: string
  campaignId: string
  adventureId: string
  adventureTitle?: string | null
  session: SessionDto
  /** The plain invite code, or null when it has been lost (the server only returns it once). */
  inviteCode: string | null
  joinLink: JoinLink
  participants: ParticipantDto[]
  availableCharacters: { id: string; name: string }[]
  loading?: boolean
  /** Set when the campaign already has a run in progress, which would make starting fail. */
  runBlockedReason?: string | null
}>()

const emit = defineEmits<{
  (e: 'approve', participantId: string): void
  (e: 'assign-character', participantId: string, characterId: string): void
  /** Ask the parent to re-read the participants (the server said the selection is out of date). */
  (e: 'refresh'): void
  (e: 'started', state: AdventureSessionStateDto): void
}>()

// Requests the host dismissed. Local only: the server cannot refuse a join request.
const dismissed = ref<Set<string>>(new Set())
const visibleParticipants = computed(() => props.participants.filter((p) => !dismissed.value.has(p.id)))
const pendingCount = computed(() => pendingParticipants(visibleParticipants.value).length)

function dismiss(participantId: string): void {
  dismissed.value = new Set(dismissed.value).add(participantId)
}

// ── Turn order ───────────────────────────────────────────────────────────────

const turnOrder = ref<string[]>([])

watch(
  () => props.participants,
  (list) => {
    turnOrder.value = reconcileTurnOrder(turnOrder.value, eligiblePlayers(list))
  },
  { immediate: true, deep: true },
)

const nameById = computed(() => new Map(props.participants.map((p) => [p.id, p.displayName])))
const characterNameById = computed(() => new Map(props.availableCharacters.map((c) => [c.id, c.name])))

const orderedPlayers = computed(() =>
  turnOrder.value.map((id) => {
    const participant = props.participants.find((p) => p.id === id)
    const character = participant?.characterId ? characterNameById.value.get(participant.characterId) : null
    return { id, name: nameById.value.get(id) ?? 'Unknown player', character: character ?? null }
  }),
)

function move(id: string, delta: -1 | 1): void {
  turnOrder.value = moveInTurnOrder(turnOrder.value, id, delta)
}

// ── Starting ─────────────────────────────────────────────────────────────────

const starting = ref(false)
const error = ref<string | null>(null)

const sessionOpen = computed(() => props.session.status !== SessionStatus.Ended)
const disabledReason = computed(
  () =>
    props.runBlockedReason ??
    startDisabledReason({
      turnOrderCount: turnOrder.value.length,
      starting: starting.value,
      sessionOpen: sessionOpen.value,
      runExists: false,
    }),
)
const canStart = computed(() => disabledReason.value === null)

async function handleStart(): Promise<void> {
  if (!canStart.value) return
  starting.value = true
  error.value = null
  try {
    const state = await startGroupAdventureSession({
      generatedAdventureId: props.adventureId,
      campaignId: props.campaignId,
      participantIds: [...turnOrder.value],
    })
    // start-group picks the campaign's latest open play session. If it handed back a run that
    // belongs to a different play session, the players waiting in THIS lobby are not in it.
    if (state.sessionId && state.sessionId !== props.sessionId) {
      error.value =
        'This campaign is already running this adventure in a different play session, so the players waiting here are not in it. ' +
        'End or finish that session first.'
      return
    }
    emit('started', state)
  } catch (err) {
    const mapped = mapGroupError(err, 'start-group')
    error.value = mapped.message
    if (mapped.kind === 'bad-request') emit('refresh')
  } finally {
    starting.value = false
  }
}
</script>

<template>
  <section class="space-y-6" aria-labelledby="lobby-heading" data-testid="group-lobby">
    <div>
      <h2 id="lobby-heading" class="text-lg font-semibold text-white">Group lobby</h2>
      <p v-if="adventureTitle" class="text-sm text-gray-400">{{ adventureTitle }}</p>
    </div>

    <!-- Invite -->
    <div v-if="inviteCode">
      <LanJoinScreen :session="session" :join-url="joinLink.url" :invite-code="inviteCode" />
      <p v-if="!joinLink.reachableFromLan" class="mt-2 text-xs text-amber-300" data-testid="no-lan-address">
        Couldn't find this computer's address on your network, so this link only works on this computer. Share your
        computer's network address instead.
      </p>
    </div>
    <div v-else class="rounded-lg border border-amber-700 bg-amber-950/30 px-4 py-3 text-sm text-amber-200" data-testid="no-invite-code">
      The invite code isn't available on this screen. The server shows it only once, when the session is created, and
      this browser no longer has it. Players who already have it can still join.
    </div>

    <!-- Players -->
    <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6">
      <ParticipantApprovalPanel
        :session-id="sessionId"
        :participants="visibleParticipants"
        :available-characters="availableCharacters"
        :loading="loading"
        dismissible
        @approve="emit('approve', $event)"
        @dismiss="dismiss"
        @assign-character="(pid: string, cid: string) => emit('assign-character', pid, cid)"
      />
      <p v-if="pendingCount === 0 && turnOrder.length === 0" class="mt-3 text-xs text-gray-500">
        Players appear here as they join. This list refreshes by itself.
      </p>
    </div>

    <!-- Turn order -->
    <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6">
      <h3 class="text-sm font-medium uppercase tracking-wider text-gray-400">Turn order</h3>
      <p class="mb-3 mt-1 text-xs text-gray-500">
        Approved players, in the order they were approved. The first one acts first. Use the arrows to change it.
      </p>

      <p v-if="orderedPlayers.length === 0" class="text-sm text-gray-500" data-testid="turn-order-empty">
        Nobody is approved yet.
      </p>
      <ol v-else class="space-y-2" data-testid="turn-order-list">
        <li
          v-for="(player, index) in orderedPlayers"
          :key="player.id"
          class="flex items-center gap-3 rounded-md border border-gray-700 bg-gray-800 px-3 py-2"
          data-testid="turn-order-item"
        >
          <span class="w-5 shrink-0 text-right text-sm text-gray-500">{{ index + 1 }}</span>
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium text-white">{{ player.name }}</span>
            <span v-if="player.character" class="block truncate text-xs text-gray-400">as {{ player.character }}</span>
          </span>
          <span class="flex shrink-0 gap-1">
            <button
              type="button"
              class="h-10 w-10 rounded-md border border-gray-600 text-gray-200 hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-30"
              :disabled="index === 0"
              :aria-label="`Move ${player.name} up`"
              data-testid="move-up"
              @click="move(player.id, -1)"
            >
              ↑
            </button>
            <button
              type="button"
              class="h-10 w-10 rounded-md border border-gray-600 text-gray-200 hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-30"
              :disabled="index === orderedPlayers.length - 1"
              :aria-label="`Move ${player.name} down`"
              data-testid="move-down"
              @click="move(player.id, 1)"
            >
              ↓
            </button>
          </span>
        </li>
      </ol>
      <p class="mt-3 text-xs text-gray-500" data-testid="host-seat-hint">
        You run the game as the host and don't take a turn. Want a seat? Join as a player from another browser with the
        invite code.
      </p>
    </div>

    <!-- Start -->
    <div class="space-y-2">
      <div v-if="error" role="alert" class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300" data-testid="start-error">
        {{ error }}
      </div>
      <button
        type="button"
        data-testid="start-adventure-button"
        class="w-full rounded-lg bg-aircane-600 px-6 py-4 text-base font-bold text-white shadow-lg hover:bg-aircane-500 focus:outline-none focus:ring-4 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 sm:w-auto"
        :disabled="!canStart"
        @click="handleStart"
      >
        {{ starting ? 'Starting…' : '▶ Start adventure' }}
      </button>
      <p v-if="disabledReason && !starting" class="text-sm text-gray-400" data-testid="start-reason">{{ disabledReason }}</p>
    </div>
  </section>
</template>

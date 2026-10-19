<script setup lang="ts">
/**
 * PlayerSessionView - the player session screen.
 * Route: /sessions/:sessionId/play
 *
 * Shows the adventure as a player may see it (scene, round, turn order, recent narration), the
 * action box for the acting player, the party, chat, dice tray, assigned character info, and the
 * roll the AI DM is waiting for. The state is the server's: hub events say "something changed" and
 * the view reads it again, with a poll as the fallback when the connection drops.
 *
 * A player who arrives without a remembered login, or whose login the server no longer accepts, is
 * sent back to the join page; a player the host has not approved yet waits there.
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './store'
import { SessionStatus, reconnectSession } from './api'
import { rollDice, RollVisibility, type RollDto, type RollRecordedEvent } from '../dice/api'
import ManualRollForm from '../dice/ManualRollForm.vue'
import ChatPanel from './ChatPanel.vue'
import type { ChatMessage } from './ChatPanel.vue'
import CombatTracker from './CombatTracker.vue'
import { getEncounter, type EncounterStateDto, type CombatTurnChangedNotification } from './combat'
import { pushEvent } from '@/features/feedback/composables/useFeedbackEventBuffer'
import {
  clearParticipantSession,
  loadParticipantSessionFor,
  type ParticipantSession,
} from './participantSession'
import {
  connectPlayerHub,
  type AdventureStateChangedEvent,
  type ChatMessageEvent,
  type PlayerHub,
  type RollRequestedEvent,
} from './playerHub'
import { isParticipantDenied, isTokenRejected } from './playerErrors'
import { onPageVisible } from './visibility'
import { actionBoxState, isMyTurn, narrationLines, turnRows, usePlayerRun } from './playerRun'

// ---------------------------------------------------------------------------
// Route / store
// ---------------------------------------------------------------------------

const route = useRoute()
const router = useRouter()
const sessionId = route.params.sessionId as string
const store = useSessionStore()

// ---------------------------------------------------------------------------
// Participant identity: the login the join page remembered (see participantSession.ts)
// ---------------------------------------------------------------------------

const me = ref<ParticipantSession | null>(null)
const participantId = computed(() => me.value?.participantId ?? null)
const participantName = computed(() => me.value?.displayName ?? null)

/** False until the remembered login has been checked, so nothing flashes before a redirect. */
const ready = ref(false)

function goToJoinPage(): void {
  void router.replace({ name: 'join-session', params: { sessionId } })
}

// ---------------------------------------------------------------------------
// Local state
// ---------------------------------------------------------------------------

const chatMessages = ref<ChatMessage[]>([])
const rollLog = ref<RollDto[]>([])
const hub = ref<PlayerHub | null>(null)
const hubUp = ref(false)
const hubError = ref<string | null>(null)

// The adventure, as the server tells a player about it
const adventure = usePlayerRun(sessionId)
const { run } = adventure
const actionText = ref('')

// Dice tray
const diceFormula = ref('1d20')
const diceContext = ref('')
const diceVisibility = ref<RollVisibility>(RollVisibility.Public)
const rollingDice = ref(false)
const diceError = ref<string | null>(null)
const lastRoll = ref<RollDto | null>(null)

// A roll the AI DM or host asked for through the live RollRequested event. (The adventure's own
// pending roll comes with the run; see rollPrompt.)
interface RollRequest {
  id: string
  label: string
  formula: string | null
  reason: string | null
  dc: number | null
}
const pendingRollRequests = ref<RollRequest[]>([])

// Active tab on mobile
const activeTab = ref<'scene' | 'chat' | 'dice' | 'party'>('scene')

// Live combat encounter (read-only for players).
const encounter = ref<EncounterStateDto | null>(null)

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

/** The participant record for the current player. */
const myParticipant = computed(() =>
  store.participants.find((p) => p.id === participantId.value) ?? null,
)

const hasCharacter = computed(() => !!myParticipant.value?.characterId)

const isConnected = computed(() => hubUp.value)

/** The player's own character id, used so the combat tracker reveals full HP only for it. */
const ownCharacterId = computed(() => myParticipant.value?.characterId ?? null)

/** The people at the table: approved participants, you first. */
const party = computed(() =>
  store.participants
    .filter((p) => p.isApproved)
    .map((p) => ({ ...p, isMe: p.id === participantId.value }))
    .sort((a, b) => Number(b.isMe) - Number(a.isMe)),
)

const rows = computed(() => turnRows(run.value, participantId.value))
const myTurn = computed(() => isMyTurn(run.value, participantId.value))
const actionBox = computed(() => actionBoxState(run.value, participantId.value, adventure.submitting.value))
const story = computed(() => narrationLines(run.value))

/** The roll being asked for: the live request (which names a formula) first, else the run's pending roll. */
const rollPrompt = computed<RollRequest | null>(() => {
  const requested = pendingRollRequests.value[0]
  if (requested) return requested
  const pending = run.value?.pendingRoll
  return pending ? { id: 'run-pending', label: pending.label, formula: null, reason: null, dc: pending.dc } : null
})

// ---------------------------------------------------------------------------
// SignalR connection
// ---------------------------------------------------------------------------

async function refreshParticipants(): Promise<void> {
  try {
    await store.fetchParticipants(sessionId)
  } catch {
    // Non-critical; the party list keeps its last state until the next refresh.
  }
}

/** Everything the screen shows that can change while the player is away from it. */
async function catchUp(): Promise<void> {
  await Promise.all([adventure.refresh(), refreshParticipants(), refreshEncounter()])
}

async function connectHub(): Promise<void> {
  if (!me.value || hub.value) return
  try {
    hub.value = await connectPlayerHub({
      sessionId,
      me: { participantId: me.value.participantId, displayName: me.value.displayName },
      onConnectionChange: (connected) => {
        hubUp.value = connected
        if (connected) {
          hubError.value = null
        } else if (!leaving) {
          // The next look was scheduled for the slow pace of a healthy connection: bring it forward.
          if (pollTimer) clearTimeout(pollTimer)
          scheduleNextPoll()
        }
      },
      onReconnected: () => void catchUp(),
      handlers: {
        // The hub sends one notification object: { sessionId, participantId, displayName, text, sentAt }.
        ChatMessageReceived: (n: ChatMessageEvent) => {
          // Diagnostics: record sender name only, never the message text.
          pushEvent({ actor: 'Player', eventType: 'ChatMessageReceived', keyField: n.displayName })
          chatMessages.value.push({
            id: `${n.participantId}-${n.sentAt}`,
            senderName: n.displayName,
            text: n.text,
            timestamp: n.sentAt,
            isOwn: n.participantId === participantId.value,
          })
        },

        // The hub sends { sessionId, roll }; someone else's private roll never arrives here.
        RollRecorded: (n: RollRecordedEvent) => {
          const roll = n.roll
          pushEvent({
            actor: 'Player',
            eventType: 'RollRecorded',
            keyField: roll.formula ? `${roll.formula} = ${roll.total}` : `total: ${roll.total}`,
          })
          rollLog.value.unshift(roll)
        },

        RollRequested: (n: RollRequestedEvent) => {
          pushEvent({ actor: 'AI', eventType: 'RollRequested', keyField: n?.formula })
          // The event goes to the whole table; only the person it is for needs to roll.
          if (n?.targetParticipantId !== participantId.value) return
          pendingRollRequests.value.push({
            id: `${n.label}-${pendingRollRequests.value.length}-${Date.now()}`,
            label: n.label,
            formula: n.formula,
            reason: n.reason,
            dc: n.dc,
          })
        },

        StateUpdated: async () => {
          pushEvent({ actor: 'Host', eventType: 'StateUpdated' })
          await Promise.all([refreshParticipants(), refreshEncounter()])
        },

        SceneChanged: async () => {
          pushEvent({ actor: 'Host', eventType: 'SceneChanged' })
          await store.fetchSession(sessionId)
        },

        CombatTurnChanged: async (n: CombatTurnChangedNotification) => {
          pushEvent({ actor: 'Host', eventType: 'CombatTurnChanged', keyField: `round ${n?.round ?? '?'}` })
          await refreshEncounter()
        },

        // The party changed: someone joined, was let in, or left.
        ParticipantJoined: () => void refreshParticipants(),
        ParticipantApproved: () => void refreshParticipants(),
        ParticipantLeft: () => void refreshParticipants(),

        // The turn, the scene or the story moved. The event carries no content: read the run again.
        AdventureStateChanged: (n: AdventureStateChangedEvent) => {
          pushEvent({ actor: 'Host', eventType: 'AdventureStateChanged', keyField: n?.reason })
          void adventure.refresh()
        },
        AdventureSceneAdvanced: () => void adventure.refresh(),
        AdventureCompleted: () => void adventure.refresh(),
      },
    })
    hubError.value = null
  } catch (err) {
    hubUp.value = false
    hubError.value = err instanceof Error ? err.message : 'Could not connect to session hub.'
  }
}

async function refreshEncounter(): Promise<void> {
  try {
    encounter.value = await getEncounter(sessionId)
  } catch {
    // Non-critical; players see the last known encounter until the next update.
  }
}

// ---------------------------------------------------------------------------
// Polling fallback: quick while the connection is down, slow as a safety net while it is up
// ---------------------------------------------------------------------------

const POLL_HUB_DOWN_MS = 5_000
const POLL_HUB_UP_MS = 30_000
let pollTimer: ReturnType<typeof setTimeout> | null = null
let leaving = false

function scheduleNextPoll(): void {
  if (leaving) return
  pollTimer = setTimeout(async () => {
    // A connection that is down also needs another try at coming up.
    if (!hubUp.value) await connectHub()
    await catchUp()
    scheduleNextPoll()
  }, hubUp.value ? POLL_HUB_UP_MS : POLL_HUB_DOWN_MS)
}

// ---------------------------------------------------------------------------
// Coming back to the page
// ---------------------------------------------------------------------------

/**
 * A phone that was switched to another app has had its connection cut, and every event the server
 * sent meanwhile is gone. On return, bring the connection back if it is not up (rejoining the
 * session's group, which a new connection is not in) and read everything again.
 */
async function handlePageVisible(): Promise<void> {
  if (!me.value || leaving) return
  if (!hubUp.value) {
    const stale = hub.value
    hub.value = null
    if (stale) await stale.stop()
    await connectHub()
  }
  await catchUp()
}

let stopWatchingVisibility: () => void = () => undefined

// ---------------------------------------------------------------------------
// Chat
// ---------------------------------------------------------------------------

async function handleSendChat(text: string): Promise<void> {
  if (!hub.value) return
  try {
    await hub.value.sendChat(text)
  } catch {
    // Silently ignore - the message will not appear in the log
  }
}

// ---------------------------------------------------------------------------
// Acting in the adventure
// ---------------------------------------------------------------------------

async function handleSubmitAction(): Promise<void> {
  if (!actionBox.value.enabled) return
  const sent = actionText.value
  const ok = await adventure.submit(sent)
  // Keep the text when it did not go through, so the player does not have to retype it.
  if (ok) actionText.value = ''
}

// ---------------------------------------------------------------------------
// Dice tray
// ---------------------------------------------------------------------------

async function handleRollDice(): Promise<void> {
  if (!participantId.value || !diceFormula.value.trim()) return
  diceError.value = null
  rollingDice.value = true
  try {
    const roll = await rollDice(sessionId, {
      rollerParticipantId: participantId.value,
      formula: diceFormula.value.trim(),
      visibility: diceVisibility.value,
      characterId: myParticipant.value?.characterId ?? null,
      context: diceContext.value.trim() || null,
    })
    lastRoll.value = roll
    diceContext.value = ''
  } catch (err) {
    diceError.value = err instanceof Error ? err.message : 'Roll failed. Check your formula.'
  } finally {
    rollingDice.value = false
  }
}

function handleManualRollRecorded(roll: RollDto): void {
  lastRoll.value = roll
}

// ---------------------------------------------------------------------------
// The roll the DM is waiting for
// ---------------------------------------------------------------------------

/** Opens the dice tray ready for the roll that was asked for. */
function handleFulfillRollRequest(request: RollRequest): void {
  if (request.formula) diceFormula.value = request.formula
  diceContext.value = request.reason?.trim() || request.label
  activeTab.value = 'dice'
  // A live request is one-shot; the run's own pending roll stays until the next action answers it.
  if (request.id !== 'run-pending') {
    pendingRollRequests.value = pendingRollRequests.value.filter((r) => r.id !== request.id)
  }
}

/**
 * Puts the last roll into the action box in words ("I rolled 14 for Athletics check") for the
 * player to read, change and send. The server reads the next action as the pending roll's result;
 * nothing here reports a roll to the adventure by itself.
 */
function handleUseRollInAction(): void {
  const roll = lastRoll.value
  const prompt = rollPrompt.value
  if (!roll) return
  actionText.value = prompt ? `I rolled ${roll.total} for ${prompt.label}.` : `I rolled ${roll.total}.`
  activeTab.value = 'scene'
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatRollTime(timestamp: string): string {
  try {
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}

function rollSummary(roll: RollDto): string {
  const who = roll.rollerName ? `${roll.rollerName}: ` : ''
  const formula = roll.formula ? `${roll.formula} = ` : ''
  const context = roll.context ? ` (${roll.context})` : ''
  return `${who}${formula}${roll.total}${context}`
}

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

/**
 * Checks the remembered login before showing anything. No login, a rejected one, a denial, or a
 * participant still waiting for approval all belong on the join page, which handles each of them.
 * A host that cannot be reached right now is not a reason to leave: the screen keeps trying.
 */
async function resume(): Promise<boolean> {
  const saved = loadParticipantSessionFor(sessionId)
  if (!saved) {
    goToJoinPage()
    return false
  }
  try {
    const status = await reconnectSession(sessionId)
    if (!status.isApproved) {
      goToJoinPage()
      return false
    }
  } catch (err) {
    if (isTokenRejected(err)) {
      clearParticipantSession()
      goToJoinPage()
      return false
    }
    if (isParticipantDenied(err)) {
      goToJoinPage()
      return false
    }
    // Unreachable or a server hiccup: carry on with what we have.
  }
  me.value = saved
  return true
}

onMounted(async () => {
  if (!(await resume())) return
  ready.value = true
  await Promise.all([
    store.fetchSession(sessionId).catch(() => undefined),
    refreshParticipants(),
    adventure.refresh(),
    refreshEncounter(),
  ])
  await connectHub()
  scheduleNextPoll()
  stopWatchingVisibility = onPageVisible(() => void handlePageVisible())
})

onUnmounted(async () => {
  leaving = true
  stopWatchingVisibility()
  if (pollTimer) clearTimeout(pollTimer)
  if (hub.value) await hub.value.stop()
})
</script>

<template>
  <div class="mx-auto w-full min-w-0 max-w-5xl space-y-4 sm:space-y-6">
    <!-- Checking the remembered login (or on the way to the join page) -->
    <p
      v-if="!ready"
      class="py-16 text-center text-sm text-gray-400"
      aria-live="polite"
      aria-busy="true"
      data-testid="player-loading"
    >
      Finding your seat at the table…
    </p>

    <template v-else>
    <!-- Page header -->
    <div class="flex items-center justify-between gap-3">
      <div class="flex min-w-0 items-center gap-3">
        <h1 class="truncate text-xl font-bold text-white sm:text-2xl">
          {{ store.currentSession?.name ?? 'Session' }}
        </h1>
        <span
          v-if="store.currentSession?.status === SessionStatus.Active"
          class="shrink-0 text-xs font-medium text-green-400"
        >
          Live
        </span>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <span
          :class="isConnected ? 'bg-green-500' : 'bg-red-500'"
          class="inline-block h-2 w-2 rounded-full"
          :title="isConnected ? 'Connected' : 'Disconnected'"
          aria-hidden="true"
        />
        <span class="max-w-[8rem] truncate text-xs text-gray-400">
          {{ participantName ?? 'Player' }}
        </span>
      </div>
    </div>

    <!-- Hub error -->
    <div v-if="hubError" role="alert">
      <div class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300">
        {{ hubError }}
        <span class="block text-xs text-red-400">Trying again every few seconds.</span>
      </div>
    </div>

    <!-- It is your turn -->
    <div
      v-if="myTurn"
      class="rounded-lg border border-aircane-500 bg-aircane-900/40 px-4 py-3 text-center"
      role="status"
      data-testid="your-turn-banner"
    >
      <p class="text-base font-semibold text-white">Your turn</p>
      <p class="text-xs text-aircane-200">Tell the DM what you do.</p>
    </div>

    <!-- The roll the DM is waiting for -->
    <div
      v-if="rollPrompt"
      aria-live="polite"
      data-testid="roll-prompt"
    >
      <div class="mb-2 flex flex-col gap-3 rounded-lg border border-yellow-700 bg-yellow-950/40 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
        <div class="min-w-0">
          <p class="text-sm font-semibold text-yellow-300 break-words">
            Roll requested: {{ rollPrompt.label }}
          </p>
          <p class="text-xs text-yellow-400">
            <span v-if="rollPrompt.formula">Formula: <code class="font-mono">{{ rollPrompt.formula }}</code></span>
            <span v-if="rollPrompt.formula && rollPrompt.dc != null"> · </span>
            <span v-if="rollPrompt.dc != null">DC {{ rollPrompt.dc }}</span>
          </p>
          <p v-if="rollPrompt.reason" class="mt-0.5 text-xs text-gray-400 break-words">{{ rollPrompt.reason }}</p>
          <p v-if="lastRoll" class="mt-1 text-xs text-gray-300">
            Your last roll: <strong>{{ lastRoll.total }}</strong>
          </p>
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          <button
            type="button"
            class="min-h-[44px] rounded-md bg-yellow-700 px-4 py-2 text-sm font-medium text-white hover:bg-yellow-600 focus:outline-none focus:ring-2 focus:ring-yellow-500 transition-colors"
            @click="handleFulfillRollRequest(rollPrompt)"
          >
            Roll Now
          </button>
          <button
            v-if="lastRoll && actionBox.enabled"
            type="button"
            class="min-h-[44px] rounded-md border border-yellow-700 px-4 py-2 text-sm font-medium text-yellow-200 hover:bg-yellow-900/40 focus:outline-none focus:ring-2 focus:ring-yellow-500 transition-colors"
            data-testid="use-roll"
            @click="handleUseRollInAction"
          >
            Use my roll
          </button>
        </div>
      </div>
    </div>

    <!-- Combat tracker (read-only for players; shown whenever an encounter is active) -->
    <CombatTracker
      v-if="encounter?.isActive"
      :encounter="encounter"
      :readonly="true"
      :own-character-id="ownCharacterId"
    />

    <!-- Mobile tab bar -->
    <nav
      class="sticky top-0 z-10 -mx-1 border-b border-surface-700/50 bg-gray-950 px-1 lg:hidden"
      aria-label="Session sections"
    >
      <div class="flex gap-1 overflow-x-auto py-2">
        <button
          v-for="tab in (['scene', 'chat', 'dice', 'party'] as const)"
          :key="tab"
          type="button"
          :class="[
            'min-h-[44px] shrink-0 rounded-md px-4 py-2 text-sm font-medium capitalize transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
            activeTab === tab
              ? 'bg-aircane-700 text-white'
              : 'text-gray-400 hover:text-gray-200',
          ]"
          :aria-pressed="activeTab === tab"
          @click="activeTab = tab"
        >
          {{ tab }}
        </button>
      </div>
    </nav>

    <!-- Content grid -->
    <div class="grid min-w-0 grid-cols-1 gap-4 sm:gap-6 lg:grid-cols-3">

      <!-- Scene + Party (left on desktop) -->
      <div class="min-w-0 space-y-4 sm:space-y-6 lg:col-span-2">

        <!-- The adventure -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'scene' }"
          class="min-w-0 rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6"
          data-testid="scene-panel"
        >
          <h2 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
            Scene
          </h2>

          <!-- Not started / not readable -->
          <p v-if="!run" class="text-sm text-gray-300 leading-relaxed" data-testid="no-adventure">
            <template v-if="!adventure.loaded.value">Loading the adventure…</template>
            <template v-else-if="adventure.refused.value">{{ adventure.loadError.value }}</template>
            <template v-else>The session is active. The host has not started the adventure yet.</template>
          </p>

          <template v-else>
            <p class="text-xs text-gray-500 break-words" data-testid="adventure-title">{{ run.adventureTitle }}</p>
            <h3 class="mt-1 text-lg font-semibold text-white break-words" data-testid="scene-title">{{ run.scene.title }}</h3>
            <p v-if="run.scene.description" class="mt-2 whitespace-pre-line text-sm leading-relaxed text-gray-300 break-words" data-testid="scene-description">
              {{ run.scene.description }}
            </p>
            <p class="mt-2 text-xs text-gray-500">
              Scene {{ run.currentSceneNumber }} of {{ run.totalScenes }}
              <span v-if="run.isGroup"> · Round {{ run.currentRound }}</span>
              <span v-if="run.status !== 'InProgress'"> · {{ run.status }}</span>
            </p>

            <!-- Turn order -->
            <div v-if="run.isGroup && rows.length" class="mt-4">
              <h3 class="mb-2 text-xs font-medium uppercase tracking-wider text-gray-400">Turn order</h3>
              <ol class="space-y-1" data-testid="turn-order">
                <li
                  v-for="row in rows"
                  :key="row.participantId"
                  :class="[
                    'flex items-center gap-3 rounded-md border px-3 py-2 text-sm',
                    row.isActive
                      ? 'border-aircane-500 bg-aircane-900/40 text-white'
                      : 'border-surface-700/50 text-gray-300',
                  ]"
                  :aria-current="row.isActive ? 'step' : undefined"
                  :data-active="row.isActive ? 'true' : 'false'"
                >
                  <span class="w-5 shrink-0 text-center text-xs text-gray-500">{{ row.position }}</span>
                  <span class="min-w-0 flex-1 truncate">
                    {{ row.participantName }}<span v-if="row.isMe" class="text-gray-400"> (you)</span>
                  </span>
                  <span v-if="row.isActive" class="shrink-0 text-xs font-semibold text-aircane-300">
                    {{ row.isMe ? 'Your turn' : 'Acting' }}
                  </span>
                </li>
              </ol>
            </div>

            <!-- The story so far -->
            <div class="mt-4">
              <h3 class="mb-2 text-xs font-medium uppercase tracking-wider text-gray-400">The story so far</h3>
              <div
                role="log"
                aria-label="Story"
                aria-live="polite"
                class="max-h-80 space-y-3 overflow-y-auto pr-1"
                data-testid="story"
              >
                <p v-if="story.length === 0 && !adventure.submitting.value" class="text-xs text-gray-600">
                  Nothing has happened yet.
                </p>
                <div
                  v-for="(line, index) in story"
                  :key="index"
                  :class="[
                    'rounded-md px-3 py-2 text-sm leading-relaxed break-words',
                    line.kind === 'PlayerAction'
                      ? 'bg-surface-800 text-gray-300'
                      : 'border-l-2 border-aircane-500 bg-surface-900 text-gray-200',
                  ]"
                  :data-kind="line.kind"
                >
                  <span class="whitespace-pre-line">{{ line.text }}</span>
                </div>
                <p
                  v-if="adventure.submitting.value"
                  class="flex items-center gap-2 text-sm text-gray-400"
                  data-testid="ai-thinking"
                >
                  <span class="inline-block h-2 w-2 animate-pulse rounded-full bg-aircane-400" aria-hidden="true" />
                  The AI DM is thinking…
                </p>
              </div>
            </div>

            <!-- Action box -->
            <form class="mt-4 space-y-2" data-testid="action-form" @submit.prevent="handleSubmitAction">
              <label for="player-action" class="block text-xs font-medium uppercase tracking-wider text-gray-400">
                {{ actionBox.enabled ? 'What do you do?' : 'Your action' }}
              </label>
              <textarea
                id="player-action"
                v-model="actionText"
                rows="3"
                maxlength="1000"
                :disabled="!actionBox.enabled"
                :placeholder="actionBox.enabled ? 'I open the door and listen…' : (actionBox.reason ?? '')"
                class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-base text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:cursor-not-allowed disabled:opacity-60"
                @keydown.enter.exact.prevent="handleSubmitAction"
              />
              <p
                v-if="actionBox.reason"
                class="text-sm text-gray-400"
                role="status"
                data-testid="action-reason"
              >
                {{ actionBox.reason }}
              </p>
              <p v-if="adventure.actionError.value" role="alert" class="text-sm text-red-400" data-testid="action-error">
                {{ adventure.actionError.value }}
              </p>
              <button
                type="submit"
                :disabled="!actionBox.enabled || !actionText.trim()"
                :aria-busy="adventure.submitting.value"
                class="min-h-[44px] w-full rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-colors sm:w-auto"
                data-testid="action-submit"
              >
                <span v-if="adventure.submitting.value">Sending…</span>
                <span v-else>Send</span>
              </button>
            </form>
            <p v-if="adventure.loadError.value && !adventure.refused.value" role="status" class="mt-2 text-xs text-orange-300">
              {{ adventure.loadError.value }}
            </p>
          </template>
        </div>

        <!-- Party + character panel -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'party' }"
          class="min-w-0 rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6"
          data-testid="party-panel"
        >
          <h2 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
            Party
          </h2>
          <ul class="mb-6 space-y-1" data-testid="party-list">
            <li v-if="party.length === 0" class="text-xs text-gray-600">No one else is here yet.</li>
            <li
              v-for="member in party"
              :key="member.id"
              class="flex items-center justify-between gap-3 rounded-md border border-surface-700/50 px-3 py-2 text-sm text-gray-300"
            >
              <span class="min-w-0 truncate">
                {{ member.displayName }}<span v-if="member.isMe" class="text-gray-400"> (you)</span>
              </span>
              <span class="shrink-0 text-xs text-gray-500">{{ member.characterId ? 'Character ready' : 'No character yet' }}</span>
            </li>
          </ul>

          <h2 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
            Character
          </h2>

          <!-- Waiting state - no character assigned -->
          <div
            v-if="!hasCharacter"
            class="flex flex-col items-center gap-3 py-6 text-center"
            aria-live="polite"
          >
            <div class="text-4xl" aria-hidden="true">⏳</div>
            <p class="text-sm font-medium text-gray-300">Waiting for character assignment</p>
            <p class="text-xs text-gray-500">
              The host will assign a character to you shortly.
            </p>
          </div>

          <!-- Character assigned -->
          <div v-else>
            <p class="text-sm text-gray-300">
              Character ID:
              <code class="font-mono text-aircane-400 break-all">{{ myParticipant?.characterId }}</code>
            </p>
            <p class="mt-2 text-xs text-gray-500">
              Full character sheet view will be available in a future update.
            </p>
          </div>
        </div>

        <!-- Dice tray -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'dice' }"
          class="min-w-0 rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6"
        >
          <h2 class="mb-4 text-sm font-medium uppercase tracking-wider text-gray-400">
            Dice Tray
          </h2>

          <!-- Quick roll -->
          <div class="mb-6 space-y-3">
            <h3 class="text-sm font-medium text-gray-300">Quick Roll</h3>

            <!-- Common dice shortcuts -->
            <div class="flex flex-wrap gap-2" role="group" aria-label="Common dice">
              <button
                v-for="die in ['d4', 'd6', 'd8', 'd10', 'd12', 'd20', 'd100']"
                :key="die"
                type="button"
                class="min-h-[44px] min-w-[44px] rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm font-medium text-gray-300 hover:border-aircane-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
                @click="diceFormula = `1${die}`"
              >
                {{ die }}
              </button>
            </div>

            <!-- Formula input -->
            <div class="flex gap-2">
              <label for="dice-formula" class="sr-only">Dice formula</label>
              <input
                id="dice-formula"
                v-model="diceFormula"
                type="text"
                placeholder="e.g. 1d20+5"
                class="min-w-0 flex-1 rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-base text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                @keydown.enter="handleRollDice"
              />
              <button
                type="button"
                :disabled="rollingDice || !diceFormula.trim()"
                class="min-h-[44px] rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                @click="handleRollDice"
              >
                <span v-if="rollingDice">Rolling…</span>
                <span v-else>Roll</span>
              </button>
            </div>

            <!-- Context -->
            <div>
              <label for="dice-context" class="sr-only">Roll context</label>
              <input
                id="dice-context"
                v-model="diceContext"
                type="text"
                placeholder="Context (optional, e.g. Attack vs goblin)"
                class="w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-base text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              />
            </div>

            <!-- Visibility -->
            <div class="flex gap-2" role="group" aria-label="Roll visibility">
              <button
                type="button"
                :class="[
                  'min-h-[44px] rounded-md px-4 py-1.5 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
                  diceVisibility === RollVisibility.Public
                    ? 'bg-aircane-600 text-white'
                    : 'border border-gray-700 bg-gray-800 text-gray-400 hover:text-gray-200',
                ]"
                :aria-pressed="diceVisibility === RollVisibility.Public"
                @click="diceVisibility = RollVisibility.Public"
              >
                Public
              </button>
              <button
                type="button"
                :class="[
                  'min-h-[44px] rounded-md px-4 py-1.5 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
                  diceVisibility === RollVisibility.Private
                    ? 'bg-aircane-600 text-white'
                    : 'border border-gray-700 bg-gray-800 text-gray-400 hover:text-gray-200',
                ]"
                :aria-pressed="diceVisibility === RollVisibility.Private"
                @click="diceVisibility = RollVisibility.Private"
              >
                Private
              </button>
            </div>

            <!-- Dice error -->
            <p v-if="diceError" role="alert" class="text-sm text-red-400">{{ diceError }}</p>

            <!-- Last roll result -->
            <output
              v-if="lastRoll"
              aria-live="polite"
              class="block rounded-lg border border-gray-700 bg-gray-800 p-4"
            >
              <p class="text-xs text-gray-500 mb-1">Last roll</p>
              <p class="text-3xl font-bold text-aircane-400">{{ lastRoll.total }}</p>
              <p v-if="lastRoll.formula" class="text-xs text-gray-500 mt-1">
                {{ lastRoll.formula }}
                <span v-if="lastRoll.dieResults?.length > 1">
                  → [{{ lastRoll.dieResults.join(', ') }}]
                </span>
              </p>
              <p v-if="lastRoll.context" class="text-xs text-gray-500">{{ lastRoll.context }}</p>
            </output>
          </div>

          <!-- Manual roll section -->
          <div class="border-t border-surface-700/50 pt-4">
            <h3 class="mb-3 text-sm font-medium text-gray-300">Manual Roll (Physical Dice)</h3>
            <ManualRollForm
              v-if="participantId"
              :session-id="sessionId"
              :roller-participant-id="participantId"
              :character-id="myParticipant?.characterId ?? null"
              @recorded="handleManualRollRecorded"
            />
            <p v-else class="text-xs text-gray-500">
              Join the session to record manual rolls.
            </p>
          </div>
        </div>
      </div>

      <!-- Right: Chat + Roll log -->
      <div class="space-y-6">
        <!-- Chat -->
        <div
          :class="{ 'hidden lg:flex': activeTab !== 'chat' }"
          class="flex h-[60vh] min-h-[320px] flex-col rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6 lg:h-[400px]"
        >
          <ChatPanel
            :messages="chatMessages"
            :disabled="!isConnected"
            placeholder="Type a message…"
            class="flex-1 min-h-0"
            @send="handleSendChat"
          />
        </div>

        <!-- Roll log -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'dice' }"
          class="rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6"
        >
          <h3 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
            Roll Log
          </h3>
          <div
            role="log"
            aria-label="Roll log"
            aria-live="polite"
            class="max-h-48 overflow-y-auto space-y-1"
          >
            <p v-if="rollLog.length === 0" class="text-xs text-gray-600 text-center py-4">
              No rolls yet.
            </p>
            <div
              v-for="roll in rollLog"
              :key="roll.id"
              class="flex items-baseline justify-between gap-2 rounded-md px-2 py-1 text-sm hover:bg-gray-800"
            >
              <span class="text-gray-300 truncate">{{ rollSummary(roll) }}</span>
              <time :datetime="roll.createdAt" class="shrink-0 text-xs text-gray-600">
                {{ formatRollTime(roll.createdAt) }}
              </time>
            </div>
          </div>
        </div>
      </div>
    </div>
    </template>
  </div>
</template>

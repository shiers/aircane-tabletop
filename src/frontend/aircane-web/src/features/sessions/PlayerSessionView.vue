<script setup lang="ts">
/**
 * PlayerSessionView - the player session screen.
 * Route: /sessions/:sessionId/play
 *
 * Shows public scene description, chat, dice tray, assigned character info,
 * and roll requests from the AI/host.
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import * as signalR from '@microsoft/signalr'
import { useSessionStore } from './store'
import { SessionStatus } from './api'
import { rollDice, RollVisibility, type RollDto } from '../dice/api'
import ManualRollForm from '../dice/ManualRollForm.vue'
import ChatPanel from './ChatPanel.vue'
import type { ChatMessage } from './ChatPanel.vue'

// ---------------------------------------------------------------------------
// Route / store
// ---------------------------------------------------------------------------

const route = useRoute()
const sessionId = route.params.sessionId as string
const store = useSessionStore()

// ---------------------------------------------------------------------------
// Participant identity (from sessionStorage after join)
// ---------------------------------------------------------------------------

const participantId = ref<string | null>(sessionStorage.getItem('participant_id'))
const participantName = ref<string | null>(sessionStorage.getItem('participant_name'))

// ---------------------------------------------------------------------------
// Local state
// ---------------------------------------------------------------------------

const chatMessages = ref<ChatMessage[]>([])
const rollLog = ref<RollDto[]>([])
const hubConnection = ref<signalR.HubConnection | null>(null)
const hubError = ref<string | null>(null)

// Dice tray
const diceFormula = ref('1d20')
const diceContext = ref('')
const diceVisibility = ref<RollVisibility>(RollVisibility.Public)
const rollingDice = ref(false)
const diceError = ref<string | null>(null)
const lastRoll = ref<RollDto | null>(null)

// Roll requests from AI/host
interface RollRequest {
  id: string
  label: string
  formula: string
  reason: string
  dc?: number
}
const pendingRollRequests = ref<RollRequest[]>([])

// Active tab on mobile
const activeTab = ref<'scene' | 'chat' | 'dice' | 'character'>('scene')

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

/** The participant record for the current player. */
const myParticipant = computed(() =>
  store.participants.find((p) => p.id === participantId.value) ?? null,
)

const hasCharacter = computed(() => !!myParticipant.value?.characterId)

const isConnected = computed(() => hubConnection.value !== null)

// ---------------------------------------------------------------------------
// SignalR connection
// ---------------------------------------------------------------------------

function buildHubUrl(): string {
  const token = sessionStorage.getItem('participant_token')
  const base = (import.meta.env.VITE_API_BASE_URL ?? '') + '/hubs/session'
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base
}

async function connectHub(): Promise<void> {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(buildHubUrl())
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  connection.on('ChatMessageReceived', (senderName: string, text: string, timestamp: string, senderId: string) => {
    chatMessages.value.push({
      id: `${senderId}-${timestamp}`,
      senderName,
      text,
      timestamp,
      isOwn: senderId === participantId.value,
    })
  })

  connection.on('RollRecorded', (roll: RollDto) => {
    rollLog.value.unshift(roll)
  })

  connection.on('RollRequested', (request: RollRequest) => {
    pendingRollRequests.value.push(request)
  })

  connection.on('StateUpdated', async () => {
    await store.fetchParticipants(sessionId)
  })

  connection.on('SceneChanged', async () => {
    await store.fetchSession(sessionId)
  })

  try {
    await connection.start()
    await connection.invoke('JoinSession', sessionId)
    hubConnection.value = connection
    hubError.value = null
  } catch (err) {
    hubError.value = err instanceof Error ? err.message : 'Could not connect to session hub.'
  }
}

// ---------------------------------------------------------------------------
// Chat
// ---------------------------------------------------------------------------

async function handleSendChat(text: string): Promise<void> {
  if (!hubConnection.value) return
  try {
    await hubConnection.value.invoke('SendChatMessage', sessionId, text)
  } catch {
    // Silently ignore
  }
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
// Roll requests
// ---------------------------------------------------------------------------

async function handleFulfillRollRequest(request: RollRequest): Promise<void> {
  if (!participantId.value) return
  diceFormula.value = request.formula
  diceContext.value = request.reason
  activeTab.value = 'dice'
  // Remove from pending
  pendingRollRequests.value = pendingRollRequests.value.filter((r) => r.id !== request.id)
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
  const formula = roll.formula ? `${roll.formula} = ` : ''
  const context = roll.context ? ` (${roll.context})` : ''
  return `${formula}${roll.total}${context}`
}

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

onMounted(async () => {
  await Promise.all([
    store.fetchSession(sessionId),
    store.fetchParticipants(sessionId),
  ])
  await connectHub()
})

onUnmounted(async () => {
  if (hubConnection.value) {
    try {
      await hubConnection.value.stop()
    } catch {
      // Ignore
    }
  }
})
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6">
    <!-- Page header -->
    <div class="flex items-center justify-between gap-4">
      <div class="flex items-center gap-3">
        <h1 class="text-2xl font-bold text-white">
          {{ store.currentSession?.name ?? 'Session' }}
        </h1>
        <span
          v-if="store.currentSession?.status === SessionStatus.Active"
          class="text-xs font-medium text-green-400"
        >
          Live
        </span>
      </div>
      <div class="flex items-center gap-2">
        <span
          :class="isConnected ? 'bg-green-500' : 'bg-red-500'"
          class="inline-block h-2 w-2 rounded-full"
          :title="isConnected ? 'Connected' : 'Disconnected'"
          aria-hidden="true"
        />
        <span class="text-xs text-gray-400">
          {{ participantName ?? 'Player' }}
        </span>
      </div>
    </div>

    <!-- Hub error -->
    <div v-if="hubError" role="alert">
      <div class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300">
        {{ hubError }}
      </div>
    </div>

    <!-- Pending roll requests banner -->
    <div
      v-if="pendingRollRequests.length > 0"
      aria-live="polite"
    >
      <div
        v-for="request in pendingRollRequests"
        :key="request.id"
        class="mb-2 flex items-center justify-between gap-4 rounded-lg border border-yellow-700 bg-yellow-950/40 px-4 py-3"
      >
        <div>
          <p class="text-sm font-semibold text-yellow-300">
            Roll requested: {{ request.label }}
          </p>
          <p class="text-xs text-yellow-400">
            Formula: <code class="font-mono">{{ request.formula }}</code>
            <span v-if="request.dc"> · DC {{ request.dc }}</span>
          </p>
          <p v-if="request.reason" class="text-xs text-gray-400 mt-0.5">{{ request.reason }}</p>
        </div>
        <button
          type="button"
          class="shrink-0 rounded-md bg-yellow-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-yellow-600 focus:outline-none focus:ring-2 focus:ring-yellow-500 transition-colors"
          @click="handleFulfillRollRequest(request)"
        >
          Roll Now
        </button>
      </div>
    </div>

    <!-- Mobile tab bar -->
    <nav
      class="sticky top-0 z-10 border-b border-surface-700/50 bg-gray-950 px-4 lg:hidden"
      aria-label="Session sections"
    >
      <div class="flex gap-1 overflow-x-auto py-2">
        <button
          v-for="tab in (['scene', 'chat', 'dice', 'character'] as const)"
          :key="tab"
          type="button"
          :class="[
            'shrink-0 rounded-md px-3 py-1.5 text-sm font-medium capitalize transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
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
    <div class="grid grid-cols-1 gap-6 lg:grid-cols-3">

      <!-- Scene + Character (left on desktop) -->
      <div class="space-y-6 lg:col-span-2">

        <!-- Scene description -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'scene' }"
          class="rounded-xl border border-surface-700/50 bg-surface-850 p-6"
        >
          <h2 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
            Scene
          </h2>
          <p class="text-sm text-gray-300 leading-relaxed">
            <!-- Placeholder until AI DM runtime provides scene descriptions -->
            The session is active. The DM will set the scene shortly.
          </p>
        </div>

        <!-- Character panel -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'character' }"
          class="rounded-xl border border-surface-700/50 bg-surface-850 p-6"
        >
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
              <code class="font-mono text-aircane-400">{{ myParticipant?.characterId }}</code>
            </p>
            <p class="mt-2 text-xs text-gray-500">
              Full character sheet view will be available in a future update.
            </p>
          </div>
        </div>

        <!-- Dice tray -->
        <div
          :class="{ 'hidden lg:block': activeTab !== 'dice' }"
          class="rounded-xl border border-surface-700/50 bg-surface-850 p-6"
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
                class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm font-medium text-gray-300 hover:border-aircane-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
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
                class="flex-1 rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                @keydown.enter="handleRollDice"
              />
              <button
                type="button"
                :disabled="rollingDice || !diceFormula.trim()"
                class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
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
                class="w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              />
            </div>

            <!-- Visibility -->
            <div class="flex gap-2" role="group" aria-label="Roll visibility">
              <button
                type="button"
                :class="[
                  'rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
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
                  'rounded-md px-3 py-1.5 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
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
          class="rounded-xl border border-surface-700/50 bg-surface-850 p-6 flex flex-col"
          style="height: 400px;"
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
        <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6 hidden lg:block">
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
  </div>
</template>

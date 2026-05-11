<script setup lang="ts">
/**
 * HostSessionView — the host/DM session control screen.
 * Route: /sessions/:sessionId/host
 *
 * Shows session info, LAN join details, participant management,
 * chat log, roll log, and a placeholder for pending AI proposals.
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as signalR from '@microsoft/signalr'
import { useSessionStore } from './store'
import { endSession, SessionStatus, type ParticipantDto } from './api'
import { getRollLog, type RollDto } from '../dice/api'
import LanJoinScreen from './LanJoinScreen.vue'
import ParticipantApprovalPanel from './ParticipantApprovalPanel.vue'
import ChatPanel from './ChatPanel.vue'
import type { ChatMessage } from './ChatPanel.vue'

// ---------------------------------------------------------------------------
// Route / store
// ---------------------------------------------------------------------------

const route = useRoute()
const router = useRouter()
const sessionId = route.params.sessionId as string
const store = useSessionStore()

// ---------------------------------------------------------------------------
// Local state
// ---------------------------------------------------------------------------

const chatMessages = ref<ChatMessage[]>([])
const rollLog = ref<RollDto[]>([])
const hubConnection = ref<signalR.HubConnection | null>(null)
const hubError = ref<string | null>(null)
const endingSession = ref(false)
const endError = ref<string | null>(null)

// Invite info is only available immediately after session creation.
// The host view reads it from the store (set by startSession action).
const showLanJoin = computed(
  () =>
    !!store.currentSession?.inviteCode &&
    !!store.currentSession?.joinUrl &&
    store.currentSession.status !== SessionStatus.Ended,
)

const joinUrl = computed(() => store.currentSession?.joinUrl ?? '')
const inviteCode = computed(() => store.currentSession?.inviteCode ?? '')

// Available characters — placeholder; a real implementation would fetch from the campaign
const availableCharacters = ref<{ id: string; name: string }[]>([])

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

  // Server → client handlers
  connection.on('ChatMessageReceived', (senderName: string, text: string, timestamp: string, senderId: string) => {
    chatMessages.value.push({
      id: `${senderId}-${timestamp}`,
      senderName,
      text,
      timestamp,
      isOwn: false,
    })
  })

  connection.on('RollRecorded', (roll: RollDto) => {
    rollLog.value.unshift(roll)
  })

  connection.on('ParticipantJoined', async () => {
    await store.fetchParticipants(sessionId)
  })

  connection.on('ParticipantLeft', async () => {
    await store.fetchParticipants(sessionId)
  })

  connection.on('StateUpdated', async () => {
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
    // Silently ignore — the message will not appear in the log
  }
}

// ---------------------------------------------------------------------------
// Participant management
// ---------------------------------------------------------------------------

async function handleApprove(participantId: string): Promise<void> {
  await store.approveParticipant(sessionId, participantId)
}

async function handleAssignCharacter(participantId: string, characterId: string): Promise<void> {
  await store.assignCharacter(sessionId, participantId, characterId)
}

// ---------------------------------------------------------------------------
// Session controls
// ---------------------------------------------------------------------------

async function handleEndSession(): Promise<void> {
  if (!confirm('End this session? All participants will be disconnected.')) return
  endingSession.value = true
  endError.value = null
  try {
    await endSession(sessionId)
    await store.fetchSession(sessionId)
    router.push('/sessions')
  } catch (err) {
    endError.value = err instanceof Error ? err.message : 'Failed to end session.'
  } finally {
    endingSession.value = false
  }
}

// ---------------------------------------------------------------------------
// Roll log helpers
// ---------------------------------------------------------------------------

function rollSummary(roll: RollDto): string {
  const formula = roll.formula ? `${roll.formula} = ` : ''
  const context = roll.context ? ` (${roll.context})` : ''
  return `${formula}${roll.total}${context}`
}

function formatRollTime(timestamp: string): string {
  try {
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

onMounted(async () => {
  await Promise.all([
    store.fetchSession(sessionId),
    store.fetchParticipants(sessionId),
    getRollLog(sessionId).then((rolls) => {
      rollLog.value = rolls
    }).catch(() => {
      // Roll log is non-critical; ignore errors on load
    }),
  ])
  await connectHub()
})

onUnmounted(async () => {
  if (hubConnection.value) {
    try {
      await hubConnection.value.stop()
    } catch {
      // Ignore stop errors
    }
  }
})
</script>

<template>
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-7xl items-center justify-between gap-4">
        <div class="flex items-center gap-3">
          <RouterLink
            to="/sessions"
            class="text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            aria-label="Back to sessions"
          >
            ← Sessions
          </RouterLink>
          <span class="text-gray-700" aria-hidden="true">/</span>
          <h1 class="text-lg font-semibold text-white">
            {{ store.currentSession?.name ?? 'Loading…' }}
          </h1>
          <span
            v-if="store.currentSession"
            :class="{
              'text-green-400': store.currentSession.status === SessionStatus.Active,
              'text-yellow-400': store.currentSession.status === SessionStatus.Pending,
              'text-orange-400': store.currentSession.status === SessionStatus.Paused,
              'text-gray-500': store.currentSession.status === SessionStatus.Ended,
            }"
            class="text-xs font-medium"
          >
            {{
              store.currentSession.status === SessionStatus.Active ? 'Active'
              : store.currentSession.status === SessionStatus.Pending ? 'Pending'
              : store.currentSession.status === SessionStatus.Paused ? 'Paused'
              : 'Ended'
            }}
          </span>
        </div>

        <!-- Session controls -->
        <div class="flex items-center gap-2">
          <button
            type="button"
            :disabled="endingSession || store.currentSession?.status === SessionStatus.Ended"
            class="rounded-md border border-red-800 bg-red-950 px-3 py-1.5 text-sm font-medium text-red-300 hover:bg-red-900 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50 transition-colors"
            @click="handleEndSession"
          >
            <span v-if="endingSession">Ending…</span>
            <span v-else>End Session</span>
          </button>
        </div>
      </div>
    </header>

    <!-- Error banner -->
    <div
      v-if="store.error || hubError || endError"
      role="alert"
      class="mx-auto max-w-7xl px-6 pt-4"
    >
      <div class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300">
        {{ store.error ?? hubError ?? endError }}
      </div>
    </div>

    <!-- Loading -->
    <div
      v-if="store.loading && !store.currentSession"
      class="flex items-center justify-center py-20"
      aria-live="polite"
      aria-busy="true"
    >
      <span class="text-gray-400">Loading session…</span>
    </div>

    <div v-else-if="store.currentSession" class="mx-auto max-w-7xl px-6 py-6">
      <!-- LAN join info (shown when invite code is available) -->
      <div v-if="showLanJoin" class="mb-6">
        <LanJoinScreen
          :session="store.currentSession"
          :join-url="joinUrl"
          :invite-code="inviteCode"
        />
      </div>

      <!-- Main grid: left column (participants + AI proposals) | right column (chat + rolls) -->
      <div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <!-- Left: Participants + AI proposals -->
        <div class="space-y-6 lg:col-span-2">
          <!-- Participant approval panel -->
          <div class="rounded-xl border border-gray-800 bg-gray-900 p-6">
            <ParticipantApprovalPanel
              :session-id="sessionId"
              :participants="store.participants"
              :available-characters="availableCharacters"
              :loading="store.loading"
              @approve="handleApprove"
              @assign-character="handleAssignCharacter"
            />
          </div>

          <!-- AI proposals placeholder -->
          <div class="rounded-xl border border-gray-800 bg-gray-900 p-6">
            <h2 class="mb-3 text-lg font-semibold text-white">Pending AI Proposals</h2>
            <p class="text-sm text-gray-500">
              AI proposals will appear here when the AI DM runtime is active.
            </p>
          </div>
        </div>

        <!-- Right: Chat + Roll log -->
        <div class="space-y-6">
          <!-- Chat -->
          <div class="rounded-xl border border-gray-800 bg-gray-900 p-6 flex flex-col" style="height: 400px;">
            <ChatPanel
              :messages="chatMessages"
              :disabled="!hubConnection"
              placeholder="Message players…"
              class="flex-1 min-h-0"
              @send="handleSendChat"
            />
          </div>

          <!-- Roll log -->
          <div class="rounded-xl border border-gray-800 bg-gray-900 p-6">
            <h3 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
              Roll Log
            </h3>
            <div
              role="log"
              aria-label="Roll log"
              aria-live="polite"
              class="max-h-64 overflow-y-auto space-y-1"
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
                <time
                  :datetime="roll.createdAt"
                  class="shrink-0 text-xs text-gray-600"
                >
                  {{ formatRollTime(roll.createdAt) }}
                </time>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </main>
</template>

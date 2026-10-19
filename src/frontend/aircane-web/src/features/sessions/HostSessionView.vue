<script setup lang="ts">
/**
 * HostSessionView - the host/DM session control screen.
 * Route: /sessions/:sessionId/host
 *
 * Shows session info, LAN join details, participant management,
 * chat log, roll log, and a placeholder for pending AI proposals.
 *
 * Group adventures add two phases, both on this screen:
 *   - Lobby: reached with `?adventure=<id>` (from "Start Group Session"). Players join with the
 *     invite code, the host approves them, orders the turns and starts the adventure.
 *   - Run: once the campaign has a group run in progress (found by asking the server, so it also
 *     survives a reload), the run view replaces the lobby: acting player, round, pass the turn,
 *     advance the scene, abandon.
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as signalR from '@microsoft/signalr'
import { useSessionStore } from './store'
import { endSession, getNetworkInfo, getParticipants, SessionStatus, type NetworkInfo } from './api'
import { getRollLog, type RollDto, type RollRecordedEvent } from '../dice/api'
import LanJoinScreen from './LanJoinScreen.vue'
import InternetPlayPanel from './components/InternetPlayPanel.vue'
import ParticipantApprovalPanel from './ParticipantApprovalPanel.vue'
import GroupLobbyPanel from './components/GroupLobbyPanel.vue'
import GroupRunHostPanel from '../adventures/components/GroupRunHostPanel.vue'
import InvitePlayersPanel from '../adventures/components/InvitePlayersPanel.vue'
import { useGroupRunStore } from '../adventures/stores/groupRunStore'
import type {
  AdventureSessionStateDto,
  ParticipantJoinedAdventureEvent,
  TurnAdvancedEvent,
} from '../adventures/api'
import { getAdventureDraft } from '../adventure-generation/api'
import { listCharacters, CharacterRole } from '../characters/api'
import { buildJoinLink, eligiblePlayers } from './lobby'
import { clearInviteCode, loadInviteCode } from './inviteCodeStorage'
import ChatPanel from './ChatPanel.vue'
import type { ChatMessage } from './ChatPanel.vue'
import CombatTracker from './CombatTracker.vue'
import {
  getEncounter,
  advanceTurn,
  applyDamage as apiApplyDamage,
  applyHealing as apiApplyHealing,
  applyCondition as apiApplyCondition,
  removeCondition as apiRemoveCondition,
  type EncounterStateDto,
  type CombatTurnChangedNotification,
} from './combat'
import { pushEvent } from '@/features/feedback/composables/useFeedbackEventBuffer'

// ---------------------------------------------------------------------------
// Route / store
// ---------------------------------------------------------------------------

const route = useRoute()
const router = useRouter()
const sessionId = route.params.sessionId as string
const store = useSessionStore()
const groupRun = useGroupRunStore()

/** Set when the host arrived from "Start Group Session": the adventure the lobby will start. */
const adventureId = computed(() => (typeof route.query.adventure === 'string' ? route.query.adventure : null))

// ---------------------------------------------------------------------------
// Local state
// ---------------------------------------------------------------------------

const chatMessages = ref<ChatMessage[]>([])
const rollLog = ref<RollDto[]>([])
const hubConnection = ref<signalR.HubConnection | null>(null)
const hubError = ref<string | null>(null)
const endingSession = ref(false)
const endError = ref<string | null>(null)

// Live combat encounter (null when no encounter is active).
const encounter = ref<EncounterStateDto | null>(null)

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

// The internet-play panel is offered whenever a live (non-ended) session exists.
const sessionIsLive = computed(
  () => !!store.currentSession && store.currentSession.status !== SessionStatus.Ended,
)

// The campaign's player characters, for assignment.
const availableCharacters = ref<{ id: string; name: string }[]>([])

// ---------------------------------------------------------------------------
// Group adventure: lobby and run
// ---------------------------------------------------------------------------

const networkInfo = ref<NetworkInfo | null>(null)
const adventureTitle = ref<string | null>(null)
const groupLoading = ref(false)

/** The server returns the invite code once; it was kept in this browser when the session was made. */
const storedInviteCode = ref<string | null>(loadInviteCode(sessionId))
const groupInviteCode = computed(() => store.currentSession?.inviteCode || storedInviteCode.value)

const joinLink = computed(() => buildJoinLink(window.location.origin, sessionId, networkInfo.value))

/** A failure to read the group run, shown only on the group screens (it is noise elsewhere). */
const groupBannerError = computed(() => (adventureId.value || showRun.value ? groupRun.error : null))

/** A group run (in progress, finished or abandoned) exists for this campaign: show the run view. */
const showRun = computed(() => !!groupRun.run?.isGroup)

/** A solo run is using the campaign, so a group run cannot start in it. */
const soloRunReason = computed(() =>
  groupRun.run && !groupRun.run.isGroup && groupRun.run.status === 'InProgress'
    ? `This campaign already has a solo adventure in progress ("${groupRun.run.adventureTitle}"). Finish or abandon it before starting a group adventure.`
    : null,
)

/** The lobby shows while the host is setting up an adventure and none is running yet. */
const showLobby = computed(
  () => !!adventureId.value && sessionIsLive.value && !showRun.value && !groupLoading.value,
)

/** Approved players who are not in the running adventure's turn order yet (they joined late). */
const latePlayers = computed(() => {
  const inRun = new Set(groupRun.run?.turnOrder.map((t) => t.participantId) ?? [])
  return eligiblePlayers(store.participants).filter((p) => !inRun.has(p.id))
})

/** People who joined and are waiting for the host (shown in the run view's invite panel). */
const pendingPlayers = computed(() => store.participants.filter((p) => !p.isApproved))

/** An approve or add is in flight, so the invite panel's buttons wait. */
const inviteBusy = computed(() => groupRun.busy !== null || store.loading)

async function loadGroupContext(): Promise<void> {
  const campaignId = store.currentSession?.campaignId
  if (!campaignId) return
  groupLoading.value = true
  try {
    // Is there already a run? (This is also what makes the run view survive a page reload.)
    await groupRun.loadForCampaign(campaignId)
  } finally {
    groupLoading.value = false
  }

  // The rest is only needed while setting up or running a group adventure.
  if (!adventureId.value && !showRun.value) return
  const [info, characters, draft] = await Promise.allSettled([
    getNetworkInfo(),
    listCharacters(campaignId),
    adventureId.value ? getAdventureDraft(adventureId.value) : Promise.resolve(null),
  ])
  networkInfo.value = info.status === 'fulfilled' ? info.value : null
  if (characters.status === 'fulfilled') {
    availableCharacters.value = characters.value
      .filter((c) => c.role === CharacterRole.Player)
      .map((c) => ({ id: c.id, name: c.name }))
  }
  if (draft.status === 'fulfilled' && draft.value) adventureTitle.value = draft.value.title
}

function handleRunStarted(state: AdventureSessionStateDto): void {
  groupRun.setRun(state)
}

// The server does not announce REST joins or approvals, so the lobby polls for them. This reads
// quietly (no loading flag, no error banner) so the buttons do not flicker.
const POLL_INTERVAL_MS = 4000
let pollTimer: ReturnType<typeof setInterval> | null = null

async function pollParticipants(): Promise<void> {
  try {
    store.participants = await getParticipants(sessionId)
  } catch {
    // A missed poll is corrected by the next one.
  }
}

function startPolling(): void {
  if (pollTimer || (!adventureId.value && !showRun.value)) return
  pollTimer = setInterval(() => {
    if (sessionIsLive.value) void pollParticipants()
  }, POLL_INTERVAL_MS)
}

function stopPolling(): void {
  if (pollTimer) clearInterval(pollTimer)
  pollTimer = null
}

/** The host's identity on the hub. The host has no participant record, so this is made up. */
const hostHubId =
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : 'host-' + Math.random().toString(16).slice(2)

/** Every run event for the group adventure just means "read the state again". */
const RUN_EVENTS = [
  'AdventureSceneAdvanced',
  'AdventureClueRevealed',
  'AdventureSecretRevealed',
  'AdventureNpcUpdated',
  'AdventureCompleted',
] as const

// ---------------------------------------------------------------------------
// SignalR connection
// ---------------------------------------------------------------------------

function buildHubUrl(): string {
  const token = sessionStorage.getItem('participant_token')
  const base = (import.meta.env.VITE_API_BASE_URL ?? '') + '/hubs/session'
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base
}

/** Joins the play session's SignalR group. The hub takes a message object, not a bare session id. */
async function joinHubGroup(connection: signalR.HubConnection): Promise<void> {
  await connection.invoke('JoinSession', {
    sessionId,
    participantId: hostHubId,
    displayName: 'Host',
    role: 0, // ParticipantRole.Host
  })
}

async function connectHub(): Promise<void> {
  try {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(buildHubUrl())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    // Server → client handlers
    // The hub sends one notification object: { sessionId, participantId, displayName, text, sentAt }.
    connection.on(
      'ChatMessageReceived',
      (n: { participantId: string; displayName: string; text: string; sentAt: string }) => {
        // Diagnostics: record sender name only, never the message text.
        pushEvent({ actor: 'Player', eventType: 'ChatMessageReceived', keyField: n.displayName })
        chatMessages.value.push({
          id: `${n.participantId}-${n.sentAt}`,
          senderName: n.displayName,
          text: n.text,
          timestamp: n.sentAt,
          isOwn: n.participantId === hostHubId,
        })
      },
    )

    // The hub sends { sessionId, roll }; a private roll reaches only its roller and the host.
    connection.on('RollRecorded', (n: RollRecordedEvent) => {
      const roll = n.roll
      pushEvent({
        actor: 'Player',
        eventType: 'RollRecorded',
        keyField: roll.formula ? `${roll.formula} = ${roll.total}` : `total: ${roll.total}`,
      })
      rollLog.value.unshift(roll)
    })

    connection.on('ParticipantJoined', async () => {
      pushEvent({ actor: 'Host', eventType: 'ParticipantJoined' })
      await store.fetchParticipants(sessionId)
    })

    connection.on('ParticipantLeft', async () => {
      pushEvent({ actor: 'Host', eventType: 'ParticipantLeft' })
      await store.fetchParticipants(sessionId)
    })

    connection.on('StateUpdated', async () => {
      pushEvent({ actor: 'Host', eventType: 'StateUpdated' })
      await store.fetchSession(sessionId)
      // Campaign state changed (which may include combat) — refresh the encounter.
      await refreshEncounter()
    })

    // The turn-changed event carries only round/turn/active-id; re-fetch the full encounter so
    // HP and conditions stay current.
    connection.on('CombatTurnChanged', async (n: CombatTurnChangedNotification) => {
      pushEvent({ actor: 'Host', eventType: 'CombatTurnChanged', keyField: `round ${n?.round ?? '?'}` })
      await refreshEncounter()
    })

    // Group adventure: the run's events carry the ADVENTURE session id, not this play session's.
    connection.on('TurnAdvanced', (event: TurnAdvancedEvent) => {
      pushEvent({ actor: 'Host', eventType: 'TurnAdvanced', keyField: event?.activeParticipantName })
      void groupRun.onRunEvent(event)
    })
    connection.on('ParticipantJoinedAdventure', (event: ParticipantJoinedAdventureEvent) => {
      pushEvent({ actor: 'Host', eventType: 'ParticipantJoinedAdventure', keyField: event?.participantName })
      void groupRun.onParticipantJoinedAdventure(event)
    })
    for (const name of RUN_EVENTS) {
      connection.on(name, (event: { sessionId: string }) => {
        pushEvent({ actor: 'AI', eventType: name })
        void groupRun.onRunEvent(event)
      })
    }

    // Group membership is per connection, so a reconnect has to join again (and catch up).
    connection.onreconnected(async () => {
      try {
        await joinHubGroup(connection)
        await Promise.all([pollParticipants(), groupRun.refresh()])
      } catch {
        // Still disconnected; the next reconnect attempt retries.
      }
    })

    await connection.start()
    await joinHubGroup(connection)
    hubConnection.value = connection
    hubError.value = null
  } catch (err) {
    hubError.value = err instanceof Error ? err.message : 'Could not connect to session hub.'
  }
}

// ---------------------------------------------------------------------------
// Combat
// ---------------------------------------------------------------------------

async function refreshEncounter(): Promise<void> {
  try {
    encounter.value = await getEncounter(sessionId)
  } catch {
    // Non-critical; leave the last known encounter in place.
  }
}

async function handleAdvanceTurn(): Promise<void> {
  encounter.value = await advanceTurn(sessionId)
}

async function handleApplyDamage(targetId: string, amount: number): Promise<void> {
  encounter.value = await apiApplyDamage(sessionId, targetId, amount)
}

async function handleApplyHealing(targetId: string, amount: number): Promise<void> {
  encounter.value = await apiApplyHealing(sessionId, targetId, amount)
}

async function handleApplyCondition(targetId: string, conditionName: string): Promise<void> {
  encounter.value = await apiApplyCondition(sessionId, targetId, conditionName)
}

async function handleRemoveCondition(targetId: string, conditionName: string): Promise<void> {
  encounter.value = await apiRemoveCondition(sessionId, targetId, conditionName)
}

// ---------------------------------------------------------------------------
// Chat
// ---------------------------------------------------------------------------

async function handleSendChat(text: string): Promise<void> {
  if (!hubConnection.value) return
  try {
    await hubConnection.value.invoke('SendChatMessage', {
      sessionId,
      participantId: hostHubId,
      displayName: 'Host',
      text,
    })
  } catch {
    // Silently ignore - the message will not appear in the log
  }
}

// ---------------------------------------------------------------------------
// Participant management
// ---------------------------------------------------------------------------

async function handleApprove(participantId: string): Promise<void> {
  try {
    await store.approveParticipant(sessionId, participantId)
  } catch {
    // store.error is shown in the banner.
  }
}

async function handleAssignCharacter(participantId: string, characterId: string): Promise<void> {
  try {
    await store.assignCharacter(sessionId, participantId, characterId)
  } catch {
    // store.error is shown in the banner.
  }
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
    // The session is over: the saved invite code is no longer useful.
    clearInviteCode(sessionId)
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
  const who = roll.rollerName ? `${roll.rollerName}: ` : ''
  const formula = roll.formula ? `${roll.formula} = ` : ''
  const context = roll.context ? ` (${roll.context})` : ''
  return `${who}${formula}${roll.total}${context}`
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
    refreshEncounter(),
  ])
  await loadGroupContext()
  startPolling()
  await connectHub()
})

onUnmounted(async () => {
  stopPolling()
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
  <div class="mx-auto max-w-6xl space-y-6">
    <!-- Page header -->
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex min-w-0 items-center gap-3">
        <h1 class="min-w-0 break-words text-xl font-bold text-white sm:text-2xl">
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

    <!-- Error banner -->
    <div
      v-if="store.error || hubError || endError || groupBannerError"
      role="alert"
    >
      <div class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300">
        {{ store.error ?? hubError ?? endError ?? groupBannerError }}
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

    <template v-else-if="store.currentSession">
      <!-- Group adventure under way: the host's run view -->
      <div
        v-if="showRun"
        class="rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6"
        data-testid="host-run-view"
      >
        <GroupRunHostPanel>
          <template #invite>
            <InvitePlayersPanel
              :invite-code="groupInviteCode"
              :join-link="joinLink"
              :pending="pendingPlayers"
              :late-players="latePlayers"
              :busy="inviteBusy"
              @approve="handleApprove"
              @add="groupRun.addParticipant($event)"
            />
          </template>
        </GroupRunHostPanel>
      </div>

      <!-- Group adventure being set up: invite, approve, order the turns, start -->
      <GroupLobbyPanel
        v-else-if="showLobby && adventureId"
        :session-id="sessionId"
        :campaign-id="store.currentSession.campaignId"
        :adventure-id="adventureId"
        :adventure-title="adventureTitle"
        :session="store.currentSession"
        :invite-code="groupInviteCode"
        :join-link="joinLink"
        :participants="store.participants"
        :available-characters="availableCharacters"
        :loading="store.loading"
        :run-blocked-reason="soloRunReason"
        @approve="handleApprove"
        @assign-character="handleAssignCharacter"
        @refresh="pollParticipants"
        @started="handleRunStarted"
      />

      <!-- LAN join info (shown when invite code is available) -->
      <div v-if="showLanJoin && !showLobby && !showRun">
        <LanJoinScreen
          :session="store.currentSession"
          :join-url="joinUrl"
          :invite-code="inviteCode"
        />
      </div>

      <!-- Internet play (Cloudflare Tunnel) for remote players -->
      <InternetPlayPanel v-if="sessionIsLive" :session-active="sessionIsLive" />

      <!-- Main grid: left column (participants + AI proposals) | right column (chat + rolls) -->
      <div class="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <!-- Left: Participants + AI proposals -->
        <div class="space-y-6 lg:col-span-2">
          <!-- Participant approval panel (the lobby has its own) -->
          <div v-if="!showLobby" class="rounded-xl border border-surface-700/50 bg-surface-850 p-4 sm:p-6">
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
          <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6">
            <h2 class="mb-3 text-lg font-semibold text-white">Pending AI Proposals</h2>
            <p class="text-sm text-gray-500">
              AI proposals will appear here when the AI DM runtime is active.
            </p>
          </div>
        </div>

        <!-- Right: Combat tracker + Chat + Roll log -->
        <div class="space-y-6">
          <!-- Combat tracker (only rendered when an encounter is active) -->
          <CombatTracker
            v-if="encounter?.isActive"
            :encounter="encounter"
            :readonly="false"
            @advance-turn="handleAdvanceTurn"
            @apply-damage="handleApplyDamage"
            @apply-healing="handleApplyHealing"
            @apply-condition="handleApplyCondition"
            @remove-condition="handleRemoveCondition"
          />

          <!-- Chat -->
          <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6 flex flex-col" style="height: 400px;">
            <ChatPanel
              :messages="chatMessages"
              :disabled="!hubConnection"
              placeholder="Message players…"
              class="flex-1 min-h-0"
              @send="handleSendChat"
            />
          </div>

          <!-- Roll log -->
          <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6">
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
    </template>
  </div>
</template>

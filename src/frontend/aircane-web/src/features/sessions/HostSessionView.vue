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
 *
 * The roster follows the session hub: joins, approvals, denials, removals and departures re-read it, and presence
 * (who has the session open) is updated in place. Only while the hub connection is down does a slow poll stand in.
 * Denying a request and removing a player are confirmed here, naming the person and what happens.
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import * as signalR from '@microsoft/signalr'
import { useSessionStore } from './store'
import { endSession, getNetworkInfo, getParticipants, SessionStatus, type NetworkInfo } from './api'
import type { RollRecordedEvent } from '../dice/api'
import type {
  AdventureStateChangedEvent,
  ChatMessageEvent,
  ParticipantPresenceChangedEvent,
} from './playerHub'
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
  TurnAdvanceChoice,
  TurnAdvancedEvent,
} from '../adventures/api'
import { getAdventureDraft } from '../adventure-generation/api'
import { listCharacters, CharacterRole } from '../characters/api'
import { buildJoinLink, eligiblePlayers } from './lobby'
import { clearInviteCode, loadInviteCode } from './inviteCodeStorage'
import { denyConfirmation, removeConfirmation } from './hostActions'
import ChatPanel from './ChatPanel.vue'
import RollLog from './components/RollLog.vue'
import { useChatHistory } from './chatHistory'
import { useRollLog } from './rollLog'
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

/** How the turn moves on in the adventure the lobby will start, as chosen when the group session was set up. */
const turnAdvance = computed<TurnAdvanceChoice>(() =>
  route.query.turnAdvance === 'AfterEachAction' ? 'AfterEachAction' : 'ManualOnly',
)

// ---------------------------------------------------------------------------
// Local state
// ---------------------------------------------------------------------------

// The host's identity on the hub. The host has no participant record, so this is made up.
const hostHubId =
  typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : 'host-' + Math.random().toString(16).slice(2)

// Chat: the stored history merged with what arrives live. The host's own lines are the ones sent under hostHubId.
const chat = useChatHistory(sessionId, (senderId) => senderId === hostHubId)
const chatMessages = chat.messages

// Roll log: the stored log merged with live rolls. The host sees every roll, private ones included.
const rolls = useRollLog(sessionId, () => ({ isHost: true, participantId: null }))
const rollLog = rolls.rolls
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

/** Who has the session open right now, by participant id (the host's roster carries it). */
const presence = computed(() =>
  Object.fromEntries(store.participants.map((p) => [p.id, p.isConnected])) as Record<string, boolean | null | undefined>,
)

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

  // The campaign's characters are what the Participants panel offers to assign, whatever the session is
  // for: without them the panel has nothing to put in its "Character" dropdown.
  try {
    const characters = await listCharacters(campaignId)
    availableCharacters.value = characters
      .filter((c) => c.role === CharacterRole.Player)
      .map((c) => ({ id: c.id, name: c.name }))
  } catch {
    // Non-critical: the panel shows the current assignments without the dropdown.
  }

  // The rest is only needed while setting up or running a group adventure.
  if (!adventureId.value && !showRun.value) return
  const [info, draft] = await Promise.allSettled([
    getNetworkInfo(),
    adventureId.value ? getAdventureDraft(adventureId.value) : Promise.resolve(null),
  ])
  networkInfo.value = info.status === 'fulfilled' ? info.value : null
  if (draft.status === 'fulfilled' && draft.value) adventureTitle.value = draft.value.title
}

function handleRunStarted(state: AdventureSessionStateDto): void {
  groupRun.setRun(state)
}

/**
 * Reads the roster again, quietly (no loading flag, no error banner) so the buttons do not flicker. The hub's
 * roster events call it; a failure is corrected by the next event or the fallback poll.
 */
async function pollParticipants(): Promise<void> {
  try {
    store.participants = await getParticipants(sessionId)
  } catch {
    // A missed read is corrected by the next one.
  }
}

// The hub tells this screen about joins, approvals, denials, removals and presence. Only while its connection is
// down (it never came up, or it is reconnecting) does a slow poll keep the roster from going stale.
const FALLBACK_POLL_MS = 15_000
let fallbackTimer: ReturnType<typeof setInterval> | null = null

function startFallbackPoll(): void {
  if (fallbackTimer) return
  fallbackTimer = setInterval(() => {
    if (sessionIsLive.value) void pollParticipants()
  }, FALLBACK_POLL_MS)
}

function stopFallbackPoll(): void {
  if (fallbackTimer) clearInterval(fallbackTimer)
  fallbackTimer = null
}

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
      (n: ChatMessageEvent) => {
        // Diagnostics: record sender name only, never the message text.
        pushEvent({ actor: 'Player', eventType: 'ChatMessageReceived', keyField: n.displayName })
        chat.addLive(n)
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
      rolls.addLive(roll)
    })

    // The roster changed: someone asked to join, was let in, was declined, removed, or left.
    for (const name of ['ParticipantJoined', 'ParticipantApproved', 'ParticipantDenied', 'ParticipantRemoved', 'ParticipantLeft']) {
      connection.on(name, () => {
        pushEvent({ actor: 'Host', eventType: name })
        void pollParticipants()
      })
    }

    // Someone's connection came up or went away: shown on the roster and the run view, and nothing else changes.
    connection.on('ParticipantPresenceChanged', (event: ParticipantPresenceChangedEvent) => {
      if (event?.participantId) store.setPresence(event.participantId, !!event.isConnected)
    })

    // The run changed in a way the table sees (a turn, a scene, a player removed): read it again.
    connection.on('AdventureStateChanged', (event: AdventureStateChangedEvent) => {
      pushEvent({ actor: 'Host', eventType: 'AdventureStateChanged', keyField: event?.reason })
      void groupRun.onRunEvent(event)
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

    // While the connection is down the roster is polled instead (slowly).
    connection.onreconnecting(() => startFallbackPoll())
    connection.onclose(() => startFallbackPoll())

    // Group membership is per connection, so a reconnect has to join again (and catch up).
    connection.onreconnected(async () => {
      try {
        await joinHubGroup(connection)
        stopFallbackPoll()
        // Whatever was said or rolled while the connection was down was not delivered: read it again.
        await Promise.all([pollParticipants(), groupRun.refresh(), chat.refresh(), rolls.refresh()])
      } catch {
        // Still disconnected; the next reconnect attempt retries.
      }
    })

    await connection.start()
    await joinHubGroup(connection)
    hubConnection.value = connection
    hubError.value = null
    stopFallbackPoll()
  } catch (err) {
    hubError.value = err instanceof Error ? err.message : 'Could not connect to session hub.'
    startFallbackPoll()
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

/**
 * Gives an approved participant's seat to a waiting request with the same name (the same person on a new device).
 * The server moves the character and the turn-order place; the roster and the run are read again to show it.
 */
async function handleReplace(newParticipantId: string, oldParticipantId: string): Promise<void> {
  try {
    await store.replaceParticipant(sessionId, oldParticipantId, newParticipantId)
  } catch {
    return   // store.error is shown in the banner.
  }
  await pollParticipants()
  if (groupRun.run) await groupRun.refresh()
}

/** Declines a waiting request, after asking. The server tells the person and revokes their login. */
async function handleDeny(participantId: string): Promise<void> {
  const person = store.participants.find((p) => p.id === participantId)
  if (!confirm(denyConfirmation(person?.displayName ?? 'this person'))) return
  try {
    await store.denyParticipant(sessionId, participantId)
  } catch {
    // store.error (the server's message) is shown in the banner.
  }
}

/**
 * Takes an approved player out of the session, after saying exactly what happens: they lose their seat, the turn
 * moves on if it is theirs, and their character is unassigned.
 */
async function handleRemove(participantId: string): Promise<void> {
  const person = store.participants.find((p) => p.id === participantId)
  const order = groupRun.run?.status === 'InProgress' ? groupRun.run.turnOrder : []
  const index = order.findIndex((t) => t.participantId === participantId)
  const holdsTurn = index >= 0 && order[index].isActive
  const nextName = holdsTurn && order.length > 1 ? order[(index + 1) % order.length].participantName : null
  const characterName = person?.characterId
    ? availableCharacters.value.find((c) => c.id === person.characterId)?.name ?? null
    : null
  const name = person?.displayName ?? (index >= 0 ? order[index].participantName : 'this player')

  if (!confirm(removeConfirmation(name, { inRun: index >= 0, holdsTurn, nextName, characterName }))) return
  try {
    await store.removeParticipant(sessionId, participantId)
  } catch {
    return   // store.error (the server's message) is shown in the banner.
  }
  if (groupRun.run) await groupRun.refresh()
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
// Lifecycle
// ---------------------------------------------------------------------------

onMounted(async () => {
  // The chat history and the roll log load alongside: they are not needed to show the rest.
  void chat.refresh()
  void rolls.refresh()
  await Promise.all([
    store.fetchSession(sessionId),
    store.fetchParticipants(sessionId),
    refreshEncounter(),
  ])
  await loadGroupContext()
  await connectHub()
})

onUnmounted(async () => {
  stopFallbackPoll()
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
        <GroupRunHostPanel :presence="presence" @remove="handleRemove">
          <template #invite>
            <InvitePlayersPanel
              :invite-code="groupInviteCode"
              :join-link="joinLink"
              :pending="pendingPlayers"
              :late-players="latePlayers"
              :busy="inviteBusy"
              :roster="store.participants"
              @approve="handleApprove"
              @deny="handleDeny"
              @replace="handleReplace"
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
        :turn-advance="turnAdvance"
        @approve="handleApprove"
        @replace="handleReplace"
        @deny="handleDeny"
        @remove="handleRemove"
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
              @replace="handleReplace"
              @deny="handleDeny"
              @remove="handleRemove"
              @assign-character="handleAssignCharacter"
            />
          </div>

          <!-- AI proposals placeholder -->
          <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6">
            <h2 class="mb-3 text-lg font-semibold text-white">Pending AI Proposals</h2>
            <p class="text-sm text-gray-500">
              AI proposals will appear here when the AI GM runtime is active.
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
              :has-more="chat.hasMore.value"
              :loading-older="chat.loadingOlder.value"
              :history-error="chat.error.value"
              placeholder="Message players…"
              class="flex-1 min-h-0"
              @send="handleSendChat"
              @load-older="chat.loadOlder"
            />
          </div>

          <!-- Roll log -->
          <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6">
            <h3 class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
              Roll Log
            </h3>
            <RollLog :rolls="rollLog" :error="rolls.error.value" />
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
  startAdventureSession,
  getAdventureSession,
  getAdventureHistory,
  getAdventureRecap,
  resumeAdventureSession,
  submitAdventureAction,
  advanceAdventureScene,
  abandonAdventureSession,
  adventureErrorMessage,
  type AdventureSessionStateDto,
  type AdventureSceneAdvancedEvent,
  type AdventureCompletedEvent,
} from '../api'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** One line in the narration feed. */
export interface NarrationEntry {
  id: string
  /** player = what the user typed, dm = AI narration, system = state changes and notices, separator = a divider line. */
  kind: 'player' | 'dm' | 'system' | 'separator'
  text: string
  timestamp: string
}

/** Every solo-adventure event the hub sends. All of them just trigger a state refresh. */
const ADVENTURE_EVENTS = [
  'AdventureSceneAdvanced',
  'AdventureClueRevealed',
  'AdventureSecretRevealed',
  'AdventureNpcUpdated',
  'AdventureCompleted',
] as const

/** Shown between a run's saved conversation and anything new. */
export const RESUME_SEPARATOR = '── Resuming session ──'

let entryCounter = 0

function newEntry(kind: NarrationEntry['kind'], text: string): NarrationEntry {
  entryCounter += 1
  return { id: `entry-${entryCounter}`, kind, text, timestamp: new Date().toISOString() }
}

function randomId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16)
  })
}

function buildHubUrl(): string {
  const token = sessionStorage.getItem('participant_token')
  const base = (import.meta.env.VITE_API_BASE_URL ?? '') + '/hubs/session'
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useAdventureSessionStore = defineStore('adventureSession', () => {
  // ── State ──────────────────────────────────────────────────────────────────
  const currentSession = ref<AdventureSessionStateDto | null>(null)
  const narrationHistory = ref<NarrationEntry[]>([])
  /** AI-written "story so far", shown above the feed when resuming a run that has history. */
  const recap = ref<string | null>(null)
  const recapLoading = ref(false)
  /** True while a request (start / load / player action / advance / abandon) is in flight. */
  const isLoading = ref(false)
  const error = ref<string | null>(null)

  let connection: signalR.HubConnection | null = null

  // ── Helpers ────────────────────────────────────────────────────────────────

  function pushEntry(kind: NarrationEntry['kind'], text: string): void {
    narrationHistory.value.push(newEntry(kind, text))
  }

  /**
   * Replaces the current state with `next`, appending a system line for anything that changed
   * (scene, clues, secrets, NPC deaths, completion). Because the lines come from comparing old
   * and new state, applying the same state twice (REST response + SignalR refresh) adds nothing.
   * A state older than the one already held (a slow refresh overtaken by a newer response) is dropped.
   */
  function applyState(next: AdventureSessionStateDto): void {
    const prev = currentSession.value

    if (prev && prev.id === next.id) {
      if (Date.parse(next.lastActivityAt) < Date.parse(prev.lastActivityAt)) return

      if (prev.currentScene.sceneId !== next.currentScene.sceneId) {
        pushEntry('system', `— ${next.currentScene.title} —`)
      }
      for (const clue of next.revealedClues) {
        if (!prev.revealedClues.some((c) => c.id === clue.id)) {
          pushEntry('system', `Clue found: ${clue.name}`)
        }
      }
      for (const secret of next.revealedSecrets) {
        if (!prev.revealedSecrets.some((s) => s.id === secret.id)) {
          pushEntry('system', `Secret uncovered: ${secret.name}`)
        }
      }
      for (const npc of next.npcStates) {
        const before = prev.npcStates.find((n) => n.npcId === npc.npcId)
        if (before?.isAlive && !npc.isAlive) pushEntry('system', `${npc.npcName} has died.`)
      }
      if (prev.status !== 'Completed' && next.status === 'Completed') {
        pushEntry('system', `Adventure complete${next.outcomeNotes ? `: ${next.outcomeNotes}` : '.'}`)
      }
    }

    currentSession.value = next
  }

  async function refresh(): Promise<void> {
    if (!currentSession.value) return
    try {
      applyState(await getAdventureSession(currentSession.value.id))
    } catch (err) {
      // A missed refresh is not fatal; the next event or action response brings the state up to date.
      error.value = adventureErrorMessage(err, 'Could not refresh the adventure.')
    }
  }

  // ── Actions ────────────────────────────────────────────────────────────────

  /**
   * Start (or resume) a run and load its state. Returns the run's ID so the caller can navigate to it.
   */
  async function startSession(
    generatedAdventureId: string,
    campaignId: string,
    characterId?: string | null,
  ): Promise<string> {
    isLoading.value = true
    error.value = null
    try {
      const run = await startAdventureSession({ generatedAdventureId, campaignId, characterId })
      resetFeed()
      currentSession.value = await getAdventureSession(run.id)
      return run.id
    } catch (err) {
      error.value = adventureErrorMessage(err, 'Could not start the adventure.')
      throw err
    } finally {
      isLoading.value = false
    }
  }

  /** Load a run's state (e.g. when opening the solo view directly). */
  async function loadSession(sessionId: string, seedOpening = true): Promise<void> {
    isLoading.value = true
    error.value = null
    try {
      const state = await getAdventureSession(sessionId)
      if (currentSession.value?.id !== state.id) resetFeed()
      currentSession.value = state
      if (seedOpening && narrationHistory.value.length === 0) {
        pushEntry('dm', state.currentScene.readAloudText?.trim() || state.currentScene.description)
      }
    } catch (err) {
      error.value = adventureErrorMessage(err, 'Could not load the adventure.')
      throw err
    } finally {
      isLoading.value = false
    }
  }

  /**
   * Open a run for play. Loads its state, then, if the narration feed is empty (a fresh page load, or
   * coming back to a run), fills it from the saved conversation, followed by a "Resuming session"
   * divider. A run with no saved conversation starts with the scene's opening text instead.
   */
  async function openSession(sessionId: string): Promise<void> {
    await loadSession(sessionId, false)
    if (narrationHistory.value.length > 0) return

    isLoading.value = true
    try {
      // Coming back to the run: drop any pending roll left over from the last visit. Awaited, so it
      // cannot race with the first action sent from this page. Failing to do so is not worth blocking play.
      try {
        await resumeAdventureSession(sessionId)
      } catch {
        // ignore
      }

      let saved: Awaited<ReturnType<typeof getAdventureHistory>> = []
      try {
        saved = await getAdventureHistory(sessionId)
      } catch {
        // The history is a convenience: play on without it rather than blocking the run.
      }

      if (saved.length > 0) {
        for (const entry of saved) {
          pushEntry(entry.entryType === 'PlayerAction' ? 'player' : 'dm', entry.content)
        }
        pushEntry('separator', RESUME_SEPARATOR)
        void loadRecap(sessionId) // an AI call: do not hold the feed back for it
      } else if (currentSession.value) {
        const scene = currentSession.value.currentScene
        pushEntry('dm', scene.readAloudText?.trim() || scene.description)
      }
    } finally {
      isLoading.value = false
    }
  }

  async function loadRecap(sessionId: string): Promise<void> {
    recapLoading.value = true
    try {
      const text = await getAdventureRecap(sessionId)
      // Ignore a late answer for a run that is no longer on screen.
      if (currentSession.value?.id === sessionId) recap.value = text.trim() || null
    } catch {
      // A missing recap is fine: the saved conversation is still there.
    } finally {
      recapLoading.value = false
    }
  }

  /** Send what the player does to the AI DM and show its narration. */
  async function sendPlayerAction(text: string): Promise<void> {
    const session = currentSession.value
    const trimmed = text.trim()
    if (!session || !trimmed || isLoading.value) return

    isLoading.value = true
    error.value = null
    pushEntry('player', trimmed)
    try {
      const response = await submitAdventureAction(session.id, trimmed)
      pushEntry('dm', response.narration)
      // Say so when the AI tried something the backend refused (e.g. the campaign's AI authority).
      for (const action of response.actions) {
        if (action.errorMessage) {
          pushEntry('system', `The AI DM's ${action.actionType} was not applied: ${action.errorMessage}`)
        }
      }
      applyState(response.state)
    } catch (err) {
      error.value = adventureErrorMessage(err, 'The AI DM could not respond.')
      throw err
    } finally {
      isLoading.value = false
    }
  }

  /**
   * Take an exit. The AI DM is asked to narrate the transition and applies the scene change itself
   * when it agrees the player is ready. If it agreed but the backend refused to apply it (the
   * campaign's AI role/authority does not allow the AI to advance scenes), the player's choice of
   * exit is applied as a host override. If the AI did not propose a move, the player stays put.
   * Resolves to true when the run is now in the target scene.
   */
  async function advanceScene(targetSceneId: string): Promise<boolean> {
    const session = currentSession.value
    if (!session || isLoading.value) return false

    const exit = session.currentScene.exits.find((e) => e.sceneId === targetSceneId)
    const title = exit?.title ?? targetSceneId

    isLoading.value = true
    error.value = null
    pushEntry('player', `I take the exit to ${title}.`)
    try {
      const response = await submitAdventureAction(
        session.id,
        `I leave through the exit to "${title}" (${targetSceneId}).`,
      )
      pushEntry('dm', response.narration)
      applyState(response.state)

      if (response.state.currentScene.sceneId === targetSceneId) return true

      const advance = response.actions.find((a) => a.actionType === 'AdvanceScene')
      if (advance && advance.outcome !== 'Applied') {
        pushEntry('system', `Moving on manually: ${advance.errorMessage ?? 'the AI could not apply the move.'}`)
        applyState(await advanceAdventureScene(session.id, targetSceneId))
        return true
      }

      return false
    } catch (err) {
      error.value = adventureErrorMessage(err, 'Could not move to that scene.')
      throw err
    } finally {
      isLoading.value = false
    }
  }

  /** Abandon the current run. */
  async function abandonSession(): Promise<void> {
    const session = currentSession.value
    if (!session) return

    isLoading.value = true
    error.value = null
    try {
      applyState(await abandonAdventureSession(session.id))
    } catch (err) {
      error.value = adventureErrorMessage(err, 'Could not abandon the adventure.')
      throw err
    } finally {
      isLoading.value = false
    }
  }

  // ── SignalR ────────────────────────────────────────────────────────────────

  /** Event handler: only events for this run matter; the transition narration (if any) is shown. */
  async function onAdventureEvent(event: { sessionId: string }): Promise<void> {
    if (!currentSession.value || event.sessionId !== currentSession.value.id) return

    const narration = (event as Partial<AdventureSceneAdvancedEvent>).narration
    if (narration) pushEntry('dm', narration)

    await refresh()
  }

  async function joinGroup(conn: signalR.HubConnection, playSessionId: string): Promise<void> {
    await conn.invoke('JoinSession', {
      sessionId: playSessionId,
      participantId: randomId(),
      displayName: 'Solo Player',
      role: 0, // ParticipantRole.Host
    })
  }

  /**
   * Subscribe to this run's live events. A no-op when the run has no play session. Failure to
   * connect is non-fatal: the REST responses keep the state current, only live pushes are lost.
   */
  async function connectHub(): Promise<void> {
    const playSessionId = currentSession.value?.sessionId
    if (!playSessionId || connection) return

    try {
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(buildHubUrl())
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build()

      for (const name of ADVENTURE_EVENTS) {
        conn.on(name, (event: AdventureSceneAdvancedEvent | AdventureCompletedEvent) => {
          void onAdventureEvent(event)
        })
      }

      // Group membership is per connection, so a reconnect has to join again (and catch up).
      conn.onreconnected(async () => {
        try {
          await joinGroup(conn, playSessionId)
          await refresh()
        } catch {
          // Still disconnected; the next reconnect attempt retries.
        }
      })

      await conn.start()
      await joinGroup(conn, playSessionId)
      connection = conn
    } catch {
      connection = null
    }
  }

  async function disconnectHub(): Promise<void> {
    const conn = connection
    connection = null
    if (!conn) return
    try {
      await conn.stop()
    } catch {
      // Ignore stop errors
    }
  }

  // ── Reset ──────────────────────────────────────────────────────────────────

  function resetFeed(): void {
    narrationHistory.value = []
    recap.value = null
    recapLoading.value = false
  }

  /** Forget the current run entirely (e.g. after leaving the solo view). */
  function clearSession(): void {
    currentSession.value = null
    resetFeed()
    error.value = null
  }

  return {
    // State
    currentSession,
    narrationHistory,
    recap,
    recapLoading,
    isLoading,
    error,
    // Actions
    startSession,
    loadSession,
    openSession,
    sendPlayerAction,
    advanceScene,
    abandonSession,
    connectHub,
    disconnectHub,
    clearSession,
  }
})

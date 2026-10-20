import { computed, ref, type Ref } from 'vue'
import {
  endMyTurn,
  getAdventureRun,
  submitPlayerAction,
  type PlayerAdventureRun,
  type PlayerNarrationEntry,
  type PlayerTurn,
} from './api'
import { mapActionError, mapEndTurnError, mapRunLoadError } from './playerErrors'

/**
 * The player's side of a group adventure: the pure rules for what they may do, and the state that
 * keeps the view in line with the server.
 */

// ---------------------------------------------------------------------------
// Pure rules
// ---------------------------------------------------------------------------

/** It is this participant's turn in a running group adventure. */
export function isMyTurn(run: PlayerAdventureRun | null, participantId: string | null): boolean {
  return (
    !!run &&
    !!participantId &&
    run.isGroup &&
    run.status === 'InProgress' &&
    run.activeParticipantId === participantId
  )
}

export interface ActionBoxState {
  enabled: boolean
  /** Shown in place of the box (or under it) when it is not usable; null when it is. */
  reason: string | null
}

/**
 * Whether the action box can be used, and if not, why. The server enforces the same rule (a 403 for
 * anyone but the acting player); this only keeps a player from typing into a box that cannot send.
 */
export function actionBoxState(
  run: PlayerAdventureRun | null,
  participantId: string | null,
  submitting: boolean,
): ActionBoxState {
  if (!run) return { enabled: false, reason: 'The host has not started the adventure yet.' }
  if (run.status !== 'InProgress') return { enabled: false, reason: 'This adventure is over.' }
  if (!run.isGroup) return { enabled: false, reason: 'The host is playing this adventure on their own.' }
  if (submitting) return { enabled: false, reason: 'The AI DM is answering…' }
  if (!isMyTurn(run, participantId)) {
    const name = run.activeParticipantName?.trim()
    return { enabled: false, reason: `Waiting for ${name || 'the next player'}` }
  }
  return { enabled: true, reason: null }
}

export interface EndTurnState {
  /**
   * What the "End my turn" control shows:
   *  - `hidden`: no group adventure is running, so there is nothing to end;
   *  - `automatic`: the run moves the turn on by itself, so there is no button;
   *  - `button`: a button, enabled when `enabled`, otherwise with `reason` ("Waiting for <name>").
   */
  kind: 'hidden' | 'automatic' | 'button'
  enabled: boolean
  reason: string | null
}

/**
 * The state of the "End my turn" control. It is enabled only for the acting player, and not while the AI is
 * answering their action or their request to end the turn is in flight. The server enforces the same rule.
 */
export function endTurnState(
  run: PlayerAdventureRun | null,
  participantId: string | null,
  busy: { submitting: boolean; ending: boolean },
): EndTurnState {
  if (!run || !run.isGroup || run.status !== 'InProgress') return { kind: 'hidden', enabled: false, reason: null }
  if (run.turnAdvance === 'AfterEachAction') {
    return { kind: 'automatic', enabled: false, reason: 'The turn passes to the next player by itself after each action.' }
  }
  if (!isMyTurn(run, participantId)) {
    const name = run.activeParticipantName?.trim()
    return { kind: 'button', enabled: false, reason: `Waiting for ${name || 'the next player'}` }
  }
  if (busy.submitting) return { kind: 'button', enabled: false, reason: 'The AI DM is answering…' }
  if (busy.ending) return { kind: 'button', enabled: false, reason: 'Ending your turn…' }
  return { kind: 'button', enabled: true, reason: null }
}

/** One row of the turn order as shown: positions are shown from 1, the server counts from 0. */
export interface TurnRow extends PlayerTurn {
  /** One-based position to display. */
  position: number
  isMe: boolean
}

export function turnRows(run: PlayerAdventureRun | null, participantId: string | null): TurnRow[] {
  return (run?.turnOrder ?? []).map((turn, index) => ({
    ...turn,
    position: index + 1,
    isMe: !!participantId && turn.participantId === participantId,
  }))
}

/** The narration to show, oldest first, as returned. */
export function narrationLines(run: PlayerAdventureRun | null): PlayerNarrationEntry[] {
  return run?.recentNarration ?? []
}

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

export interface PlayerRunState {
  run: Ref<PlayerAdventureRun | null>
  loaded: Ref<boolean>
  loadError: Ref<string | null>
  submitting: Ref<boolean>
  actionError: Ref<string | null>
  /** True once the server said this participant may not read the run at all. */
  refused: Ref<boolean>
  endingTurn: Ref<boolean>
  endTurnError: Ref<string | null>
  refresh: () => Promise<void>
  submit: (text: string) => Promise<boolean>
  /** Ends the caller's own turn. Returns true when the turn moved on. */
  endTurn: () => Promise<boolean>
}

/**
 * Keeps the run in step with the server. Every hub event just means "read it again" (the events carry
 * no content), and a slow poll covers a dropped connection, so the state shown is always the server's.
 *
 * @param sessionId the play session
 * @param getParticipantId the caller's participant id, used only to word things and gate the box
 */
export function usePlayerRun(sessionId: string): PlayerRunState {
  const run = ref<PlayerAdventureRun | null>(null)
  const loaded = ref(false)
  const loadError = ref<string | null>(null)
  const submitting = ref(false)
  const actionError = ref<string | null>(null)
  const refused = ref(false)
  const endingTurn = ref(false)
  const endTurnError = ref<string | null>(null)

  // Overlapping reads would let an older answer replace a newer one: keep only the latest.
  let readCounter = 0

  async function refresh(): Promise<void> {
    const mine = ++readCounter
    try {
      const next = await getAdventureRun(sessionId)
      if (mine !== readCounter) return
      run.value = next
      loadError.value = null
      refused.value = false
    } catch (err) {
      if (mine !== readCounter) return
      loadError.value = mapRunLoadError(err)
      refused.value = (err as { response?: { status?: number } })?.response?.status === 403
    } finally {
      if (mine === readCounter) loaded.value = true
    }
  }

  /** Sends the action. Returns true when the AI DM answered; false (with `actionError` set) when not. */
  async function submit(text: string): Promise<boolean> {
    const current = run.value
    const trimmed = text.trim()
    if (!current || !trimmed || submitting.value) return false

    submitting.value = true
    actionError.value = null
    try {
      await submitPlayerAction(current.runId, trimmed)
      return true
    } catch (err) {
      actionError.value = mapActionError(err)
      return false
    } finally {
      submitting.value = false
      // Whatever happened, the turn may have moved or the story grown: read it again.
      await refresh()
    }
  }

  /**
   * Ends the caller's own turn. The server answers with the table view, which is taken as the new run state
   * (the turn has moved on). On a refusal, including "the turn already moved on", the message says why and the
   * run is read again so the screen shows who has the turn now.
   */
  async function endTurn(): Promise<boolean> {
    const current = run.value
    if (!current || endingTurn.value) return false

    endingTurn.value = true
    endTurnError.value = null
    try {
      const next = await endMyTurn(current.runId)
      if (next) run.value = next
      else await refresh()
      return true
    } catch (err) {
      endTurnError.value = mapEndTurnError(err)
      await refresh()
      return false
    } finally {
      endingTurn.value = false
    }
  }

  return { run, loaded, loadError, submitting, actionError, refused, endingTurn, endTurnError, refresh, submit, endTurn }
}

/** Convenience for components: the action box state as a computed. */
export function useActionBox(
  state: Pick<PlayerRunState, 'run' | 'submitting'>,
  participantId: Ref<string | null>,
) {
  return computed(() => actionBoxState(state.run.value, participantId.value, state.submitting.value))
}

import { computed, ref, type Ref } from 'vue'
import {
  getAdventureRun,
  submitPlayerAction,
  type PlayerAdventureRun,
  type PlayerNarrationEntry,
  type PlayerTurn,
} from './api'
import { mapActionError, mapRunLoadError } from './playerErrors'

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
  refresh: () => Promise<void>
  submit: (text: string) => Promise<boolean>
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

  return { run, loaded, loadError, submitting, actionError, refused, refresh, submit }
}

/** Convenience for components: the action box state as a computed. */
export function useActionBox(
  state: Pick<PlayerRunState, 'run' | 'submitting'>,
  participantId: Ref<string | null>,
) {
  return computed(() => actionBoxState(state.run.value, participantId.value, state.submitting.value))
}

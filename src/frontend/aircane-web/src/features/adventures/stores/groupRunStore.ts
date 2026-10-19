import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import {
  getActiveAdventureSession,
  getAdventureSession,
  advanceAdventureTurn,
  advanceAdventureScene,
  abandonAdventureSession,
  addAdventureParticipant,
  type AdventureSessionStateDto,
  type ParticipantJoinedAdventureEvent,
} from '../api'
import { mapGroupError, type GroupAction } from '../groupErrors'

/** Which host action is in flight, so its button can show progress and the others wait. */
export type GroupRunBusy = 'turn' | 'scene' | 'abandon' | 'add' | null

/**
 * The host's view of a group adventure run: its state, and the actions only the host may take
 * (pass the turn, move the scene, abandon, add a late player). The server pushes events when
 * anything changes, and every event simply triggers {@link refresh}, so the state shown is
 * always the server's.
 */
export const useGroupRunStore = defineStore('groupRun', () => {
  const run = ref<AdventureSessionStateDto | null>(null)
  const busy = ref<GroupRunBusy>(null)
  const error = ref<string | null>(null)
  /** A short note about something that just happened (a player was added). */
  const notice = ref<string | null>(null)

  /** The run is a group run that can still be played. */
  const isActiveGroupRun = computed(
    () => !!run.value && run.value.isGroup && run.value.status === 'InProgress',
  )

  /** Who gets the turn after the active player (wrapping), or null when nobody is in the order. */
  const nextParticipantName = computed(() => {
    const order = run.value?.turnOrder ?? []
    if (!order.length) return null
    const activeIndex = order.findIndex((t) => t.isActive)
    return order[(activeIndex + 1) % order.length].participantName
  })

  /** Whether passing the turn starts a new round (the active player is last, or alone). */
  const nextStartsNewRound = computed(() => {
    const order = run.value?.turnOrder ?? []
    if (!order.length) return false
    const activeIndex = order.findIndex((t) => t.isActive)
    return activeIndex === order.length - 1
  })

  function fail(err: unknown, action: GroupAction): void {
    error.value = mapGroupError(err, action).message
  }

  /**
   * Loads the campaign's in-progress run, if there is one. Returns it, or null (also null when it
   * could not be loaded, in which case `error` says why).
   */
  async function loadForCampaign(campaignId: string): Promise<AdventureSessionStateDto | null> {
    error.value = null
    try {
      const active = await getActiveAdventureSession(campaignId)
      run.value = active ? await getAdventureSession(active.id) : null
    } catch (err) {
      run.value = null
      fail(err, 'load')
    }
    return run.value
  }

  /** Re-reads the current run. Quiet on failure: a missed refresh is corrected by the next event. */
  async function refresh(): Promise<void> {
    if (!run.value) return
    try {
      run.value = await getAdventureSession(run.value.id)
    } catch {
      // Keep what is on screen.
    }
  }

  /** Runs one host action, keeping the state it returns and turning a failure into `error`. */
  async function act(
    kind: Exclude<GroupRunBusy, null>,
    action: GroupAction,
    call: (runId: string) => Promise<AdventureSessionStateDto>,
  ): Promise<boolean> {
    if (!run.value || busy.value) return false
    busy.value = kind
    error.value = null
    notice.value = null
    try {
      run.value = await call(run.value.id)
      return true
    } catch (err) {
      fail(err, action)
      return false
    } finally {
      busy.value = null
    }
  }

  const advanceTurn = () => act('turn', 'advance-turn', (id) => advanceAdventureTurn(id))
  const advanceScene = (targetSceneId: string) =>
    act('scene', 'advance-scene', (id) => advanceAdventureScene(id, targetSceneId))
  const abandon = () => act('abandon', 'abandon', (id) => abandonAdventureSession(id))
  const addParticipant = (participantId: string) =>
    act('add', 'add-participant', (id) => addAdventureParticipant(id, participantId))

  /** Hub event: a player was added to the end of the turn order. */
  async function onParticipantJoinedAdventure(event: ParticipantJoinedAdventureEvent): Promise<void> {
    if (!run.value || event.sessionId !== run.value.id) return
    // turnPosition is zero-based: the first player is 0.
    notice.value = `${event.participantName} joined the adventure as player ${event.turnPosition + 1}.`
    await refresh()
  }

  /** Hub event for this run (turn passed, scene changed, clue found…): just re-read the state. */
  async function onRunEvent(event: { sessionId: string }): Promise<void> {
    if (!run.value || event.sessionId !== run.value.id) return
    await refresh()
  }

  function setRun(state: AdventureSessionStateDto | null): void {
    run.value = state
    error.value = null
    notice.value = null
  }

  function reset(): void {
    run.value = null
    busy.value = null
    error.value = null
    notice.value = null
  }

  return {
    run,
    busy,
    error,
    notice,
    isActiveGroupRun,
    nextParticipantName,
    nextStartsNewRound,
    loadForCampaign,
    refresh,
    advanceTurn,
    advanceScene,
    abandon,
    addParticipant,
    onParticipantJoinedAdventure,
    onRunEvent,
    setRun,
    reset,
  }
})

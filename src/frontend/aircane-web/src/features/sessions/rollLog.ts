import { ref, type Ref } from 'vue'
import { getRollLog, RollVisibility, type RollDto } from '../dice/api'

/**
 * The roll log as a screen shows it: the stored log (GET /api/sessions/{id}/rolls) merged with the rolls
 * that arrive live (`RollRecorded`).
 *
 * **Who sees which roll.** A private roll is for the person who rolled it and the host. The server already
 * leaves other people's private rolls out of the log it returns and out of the broadcast; this filters again
 * on the client so that a roll that reached a screen by mistake (an older server, a bug) is still not shown.
 */

export interface RollViewer {
  /** The host sees every roll. */
  isHost: boolean
  /** The viewer's participant id: their own private rolls are theirs to see. Null for the host. */
  participantId: string | null
}

/** Whether this viewer may see this roll. */
export function canSeeRoll(roll: RollDto, viewer: RollViewer): boolean {
  if (roll.visibility !== RollVisibility.Private) return true
  return viewer.isHost || (viewer.participantId !== null && roll.rollerParticipantId === viewer.participantId)
}

/** Rolls the viewer may see. */
export function visibleRolls(rolls: readonly RollDto[], viewer: RollViewer): RollDto[] {
  return rolls.filter((roll) => canSeeRoll(roll, viewer))
}

/** Merges two lists of rolls without duplicates (by id), newest first. */
export function mergeRolls(existing: readonly RollDto[], incoming: readonly RollDto[]): RollDto[] {
  const byId = new Map<string, RollDto>()
  for (const roll of [...existing, ...incoming]) {
    if (!byId.has(roll.id)) byId.set(roll.id, roll)
  }
  return [...byId.values()].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
}

/**
 * The words for a roll: who rolled, what, the dice, and the total, e.g. "Bifur: 1d20+2 [15] +2 = 17 (Perception)".
 * `selfParticipantId` turns the viewer's own rolls into "You".
 */
export function describeRoll(roll: RollDto, selfParticipantId: string | null = null): string {
  const who = roll.rollerParticipantId === selfParticipantId && selfParticipantId !== null
    ? 'You'
    : roll.rollerName ?? 'Host'
  const formula = roll.formula ?? 'Physical dice'
  const dice = roll.dieResults?.length ? ` [${roll.dieResults.join(', ')}]` : ''
  const modifier = roll.modifier ? ` ${roll.modifier > 0 ? '+' : '-'}${Math.abs(roll.modifier)}` : ''
  const context = roll.context ? ` (${roll.context})` : ''
  return `${who}: ${formula}${dice}${modifier} = ${roll.total}${context}`
}

/** Roll log state for one screen. */
export function useRollLog(
  sessionId: string,
  viewer: () => RollViewer,
  fetchLog: typeof getRollLog = getRollLog,
): { rolls: Ref<RollDto[]>; error: Ref<string | null>; refresh: () => Promise<void>; addLive: (roll: RollDto) => void } {
  const rolls = ref<RollDto[]>([])
  const error = ref<string | null>(null)

  /** Loads the stored log and merges it with what is on screen. On opening the tab and after a reconnect. */
  async function refresh(): Promise<void> {
    try {
      const stored = await fetchLog(sessionId)
      rolls.value = mergeRolls(rolls.value, visibleRolls(stored, viewer()))
      error.value = null
    } catch {
      error.value = 'Could not load the roll log.'
    }
  }

  /** Adds a roll that arrived live, if this viewer may see it. */
  function addLive(roll: RollDto): void {
    if (!canSeeRoll(roll, viewer())) return
    rolls.value = mergeRolls(rolls.value, [roll])
  }

  return { rolls, error, refresh, addLive }
}

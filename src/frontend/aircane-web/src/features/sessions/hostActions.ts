/**
 * What the host is asked before declining a join request or removing a player: who, and exactly what happens.
 * Kept here so the lobby, the Participants panel and the run view all say the same thing.
 */

export function denyConfirmation(displayName: string): string {
  return (
    `Decline ${displayName}'s request to join?\n\n` +
    'They are told the request was declined and cannot take part. They can ask again with the invite code.'
  )
}

export interface RemovalContext {
  /** They are in the running adventure's turn order. */
  inRun: boolean
  /** It is their turn right now. */
  holdsTurn: boolean
  /** Who gets the turn if it is theirs; null when nobody else is in the order. */
  nextName: string | null
  /** The character assigned to them, if any. */
  characterName: string | null
  /** Whether the Characters feature is on; when it is off the confirmation says nothing about characters. */
  charactersEnabled?: boolean
}

export function removeConfirmation(displayName: string, context: RemovalContext): string {
  const lines = [`• ${displayName} loses their seat and is disconnected.`]
  if (context.holdsTurn) {
    lines.push(
      context.nextName
        ? `• It is their turn: the turn moves on to ${context.nextName}.`
        : '• It is their turn, and nobody else is in the turn order: the adventure waits until you add a player.',
    )
  } else if (context.inRun) {
    lines.push('• They leave the adventure\'s turn order.')
  }
  if (context.charactersEnabled !== false) {
    lines.push(
      context.characterName
        ? `• Their character, ${context.characterName}, is unassigned.`
        : '• Any character assigned to them is unassigned.',
    )
  }
  return (
    `Remove ${displayName} from this session?\n\n${lines.join('\n')}\n\n` +
    'They can ask to join again with the invite code; you would have to approve them.'
  )
}

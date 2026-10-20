/**
 * The parts of attaching, detaching and deleting an adventure that the campaign page and the Forge share:
 * asking the host to agree to what the server flagged, and the wording of each confirmation.
 */
import {
  assignAdventureToCampaign,
  conflictOf,
  type AdventureConflict,
  type AdventureDeleteImpact,
  type AssignConfirmations,
  type CampaignAdventure,
} from './api'

/** The ask for a confirmation: true to go ahead. Defaults to the browser's `confirm`. */
export type Ask = (message: string) => boolean

export function messageOf(e: unknown, fallback: string): string {
  const detail = (e as { response?: { data?: { detail?: string } } })?.response?.data?.detail
  return detail ?? (e instanceof Error ? e.message : fallback)
}

/** What to ask the host before going ahead with something the server flagged. */
export function describeConflict(conflict: AdventureConflict, adventureTitle: string, campaignName: string): string {
  if (conflict.code === 'adventure-in-other-campaign') {
    const other = conflict.currentCampaignName ?? 'another campaign'
    return (
      `"${adventureTitle}" is currently in the campaign "${other}".\n\n` +
      `Attaching it to "${campaignName}" takes it out of "${other}". Its past runs stay where they were played.\n\n` +
      'Move it?'
    )
  }
  return (
    `"${adventureTitle}" is for ${conflict.adventureGameSystem ?? 'a different game system'}, ` +
    `but "${campaignName}" uses ${conflict.campaignGameSystem ?? 'another one'}.\n\n` +
    'Attach it anyway?'
  )
}

/**
 * Attach an adventure to a campaign. Each thing the server wants agreed (leaving another campaign, a different
 * game system) is put to the host by name, and the request is sent again with that agreement. Resolves to the
 * updated adventure, or null when the host declines. Anything else the server refuses, including a run in
 * progress, is thrown.
 */
export async function attachWithConfirmation(
  adventure: Pick<CampaignAdventure, 'id' | 'title'>,
  campaignId: string,
  campaignName: string,
  ask: Ask = (message) => window.confirm(message),
): Promise<CampaignAdventure | null> {
  const agreed: AssignConfirmations = {}
  // At most one round for each thing that can need agreeing, plus the final one that goes through.
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      return await assignAdventureToCampaign(adventure.id, campaignId, agreed)
    } catch (e) {
      const conflict = conflictOf(e)
      if (!conflict || conflict.code === 'adventure-run-in-progress') throw e
      if (!ask(describeConflict(conflict, adventure.title, campaignName))) return null
      if (conflict.code === 'adventure-in-other-campaign') agreed.confirmMove = true
      else agreed.confirmMismatch = true
    }
  }
  throw new Error('The adventure could not be attached.')
}

function plural(n: number, one: string, many = `${one}s`): string {
  return `${n} ${n === 1 ? one : many}`
}

/** The confirmation before an adventure is deleted for good: names what goes, and that it cannot be undone. */
export function describeDelete(impact: AdventureDeleteImpact): string {
  const lines: string[] = []

  if (impact.totalRuns > 0) {
    const parts = [
      impact.completedRuns > 0 ? `${impact.completedRuns} completed` : '',
      impact.abandonedRuns > 0 ? `${impact.abandonedRuns} abandoned` : '',
      impact.notStartedRuns > 0 ? `${impact.notStartedRuns} not started` : '',
    ].filter(Boolean)
    lines.push(`• ${plural(impact.totalRuns, 'run')}${parts.length ? ` (${parts.join(', ')})` : ''}`)
    lines.push(`• ${plural(impact.transcriptEntries, 'transcript entry', 'transcript entries')}`)
  }
  lines.push(
    impact.indexedChunks > 0
      ? `• the indexed content the AI searches (${plural(impact.indexedChunks, 'piece')})`
      : '• its indexed content, if any',
  )

  return (
    `Permanently delete "${impact.title}"?\n\nThis removes the adventure itself and:\n${lines.join('\n')}\n\n` +
    'This cannot be undone. To keep it, use "Remove from campaign" instead.'
  )
}

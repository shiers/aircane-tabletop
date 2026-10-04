// ---------------------------------------------------------------------------
// Import-source badge metadata.
//
// `detectedSource` arrives as a string (the backend serializes the
// CharacterImportSource enum as its name, e.g. "PathbuilderTwo", because the API
// has no global JsonStringEnumConverter). Badge colors per the design note:
//   PathbuilderTwo → orange, DndBeyond* → red, Foundry* → purple,
//   Roll20 → blue, GenericVtt → gray, Unknown → amber.
// ---------------------------------------------------------------------------

export interface SourceBadge {
  /** Human-readable label shown on the badge. */
  label: string
  /** Tailwind utility classes for the badge background/text. */
  classes: string
}

/** The user-selectable sources shown in the "override auto-detect" dropdown. */
export const SOURCE_OPTIONS: { label: string; value: string }[] = [
  { label: 'Auto-detect', value: '' },
  { label: 'Pathbuilder 2e', value: 'PathbuilderTwo' },
  { label: 'D&D Beyond (File)', value: 'DndBeyondApi' },
  { label: 'Foundry VTT (D&D 5e)', value: 'FoundryDnd5e' },
  { label: 'Foundry VTT (PF2e)', value: 'FoundryPf2e' },
  { label: 'Roll20', value: 'Roll20' },
  { label: 'Generic VTT', value: 'GenericVtt' },
]

const BADGES: Record<string, SourceBadge> = {
  PathbuilderTwo: { label: 'Pathbuilder 2e', classes: 'bg-orange-500/20 text-orange-300' },
  DndBeyondApi: { label: 'D&D Beyond', classes: 'bg-red-500/20 text-red-300' },
  DndBeyondCompanion: { label: 'D&D Beyond', classes: 'bg-red-500/20 text-red-300' },
  FoundryDnd5e: { label: 'Foundry VTT (D&D 5e)', classes: 'bg-purple-500/20 text-purple-300' },
  FoundryPf2e: { label: 'Foundry VTT (PF2e)', classes: 'bg-purple-500/20 text-purple-300' },
  Roll20: { label: 'Roll20', classes: 'bg-blue-500/20 text-blue-300' },
  GenericVtt: { label: 'Generic VTT', classes: 'bg-gray-500/20 text-gray-300' },
  Unknown: { label: 'Unknown', classes: 'bg-amber-500/20 text-amber-300' },
}

/** Resolve the badge for a `detectedSource` string. Falls back to the Unknown badge. */
export function sourceBadge(detectedSource: string | null | undefined): SourceBadge {
  if (!detectedSource) return BADGES.Unknown
  return BADGES[detectedSource] ?? BADGES.Unknown
}

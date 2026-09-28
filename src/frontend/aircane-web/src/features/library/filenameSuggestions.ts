/**
 * Helpers for suggesting a clean document Title (and, when present, a Ruleset year)
 * from an uploaded file's name.
 *
 * These are best-effort heuristics for the upload form only. They never overwrite a value
 * the user has already typed, and the user can always edit the suggestion. Nothing here is
 * authoritative — the goal is to reduce typing and nudge toward consistent titles, not to
 * guess perfectly.
 */

/**
 * Common abbreviation expansions applied as whole-word, case-insensitive replacements.
 * Uses a replacer function so an optional edition suffix (e.g. "5e") is only emitted when it
 * was actually present in the source — we never invent an edition.
 */
const ABBREVIATION_EXPANSIONS: ReadonlyArray<{
  pattern: RegExp
  replacement: (match: RegExpMatchArray) => string
}> = [
  // "DnD", "D&D", "D and D", optionally followed by an edition like "5e" (with or without a
  // space). "DnD 5e" / "D&D5e" → "D&D 5e"; standalone "DnD" → "D&D".
  {
    pattern: /\bd\s*(?:&|n|and)\s*d(?:\s*(\d+e))?\b/gi,
    replacement: (m) => (m[1] ? `D&D ${m[1].toLowerCase()}` : 'D&D'),
  },
  // "Pf2e" / "PF 2e" → "Pathfinder 2e"
  {
    pattern: /\bpf\s*(\d+e)\b/gi,
    replacement: (m) => `Pathfinder ${m[1].toLowerCase()}`,
  },
]

/**
 * Removes the final file extension (e.g. ".pdf"). Only the last segment is stripped so titles
 * containing dots (e.g. version numbers) are preserved.
 */
function stripExtension(fileName: string): string {
  return fileName.replace(/\.[^.]+$/, '')
}

/**
 * Removes parenthetical/bracketed qualifiers that describe the scan or edition variant rather
 * than the work itself — e.g. "(Color OCR)", "(BnW OCR)", "[Deluxe]", "(scan)". These are the
 * main source of near-duplicate titles.
 */
function stripQualifiers(value: string): string {
  return value
    // Match a bracket group whose body contains no bracket chars. Excluding the opening
    // brackets from the body keeps this linear (no catastrophic backtracking).
    .replace(/[([{][^()[\]{}]*[)\]}]/g, ' ')
    .trim()
}

/**
 * Collapses separator characters (underscores, extra whitespace, stray dashes at the ends)
 * into single spaces and trims the result.
 */
function tidySeparators(value: string): string {
  const collapsed = value
    .replace(/_+/g, ' ') // underscores → spaces
    .replace(/\s+/g, ' ') // collapse whitespace
    .trim()
  // Strip leading/trailing dash + space runs left behind by qualifier removal, without a
  // trailing-anchored quantifier (avoids regex-backtracking lint warnings).
  return trimChars(collapsed, '-').trim()
}

/**
 * Removes all leading and trailing occurrences of a single delimiter character. Linear scan,
 * no regex, so no backtracking concerns.
 */
function trimChars(value: string, ch: string): string {
  let start = 0
  let end = value.length
  while (start < end && value[start] === ch) start++
  while (end > start && value[end - 1] === ch) end--
  return value.slice(start, end)
}

function expandAbbreviations(value: string): string {
  let result = value
  for (const { pattern, replacement } of ABBREVIATION_EXPANSIONS) {
    result = result.replace(pattern, (...args) => {
      // String.replace passes (match, ...groups, offset, string); reconstruct a match-like array.
      const groups = args.slice(0, -2) as string[]
      return replacement(groups as unknown as RegExpMatchArray)
    })
  }
  // Expansion can introduce doubled spaces; tidy again.
  return result.replace(/\s+/g, ' ').trim()
}

/**
 * Suggests a human-readable document title from a filename.
 *
 * Example: "DnD 5e Dungeon Masters Guide (Color OCR).pdf" → "D&D 5e Dungeon Masters Guide".
 *
 * Returns an empty string for empty/whitespace input.
 */
export function suggestTitleFromFilename(fileName: string): string {
  if (!fileName?.trim()) return ''

  const withoutExt = stripExtension(fileName)
  const withoutQualifiers = stripQualifiers(withoutExt)
  const tidied = tidySeparators(withoutQualifiers)
  return expandAbbreviations(tidied)
}

/**
 * Extracts a plausible edition/ruleset year (a standalone 4-digit year in a reasonable range)
 * from a filename. Returns the year as a string (e.g. "2014") or null when no year is present.
 *
 * This only fires when a year is actually in the name — it never infers an edition that isn't
 * written down. "... Dungeon Masters Guide.pdf" → null (the user must choose the edition).
 */
const YEAR_PATTERN = /(?<!\d)(?:19|20)\d{2}(?!\d)/

export function detectRulesetYear(fileName: string): string | null {
  if (!fileName) return null
  // Match a standalone 19xx/20xx year not glued to other digits.
  const match = YEAR_PATTERN.exec(fileName)
  return match ? match[0] : null
}

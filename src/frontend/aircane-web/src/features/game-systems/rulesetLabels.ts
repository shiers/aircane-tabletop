// Ruleset labels on a game system definition (metadata.rulesets): the labels under which the
// system's books are filed in the library. The editor shows them as one comma-separated field.

/** The most labels a definition may declare (the backend validator enforces the same limit). */
export const MAX_RULESET_LABELS = 10

/** The longest a single label may be (the backend validator enforces the same limit). */
export const MAX_RULESET_LABEL_LENGTH = 60

/** Splits a comma-separated field into trimmed, non-empty labels, dropping case-insensitive repeats. */
export function parseRulesetLabels(text: string): string[] {
  const seen = new Set<string>()
  const labels: string[] = []
  for (const part of text.split(',')) {
    const label = part.trim()
    if (!label || seen.has(label.toLowerCase())) continue
    seen.add(label.toLowerCase())
    labels.push(label)
  }
  return labels
}

/** Joins labels back into the comma-separated field. */
export function formatRulesetLabels(labels: readonly string[] | null | undefined): string {
  return (labels ?? []).join(', ')
}

/** Reads metadata.rulesets from a parsed definition, keeping only string entries. */
export function readRulesetLabels(parsedDefinition: unknown): string[] {
  const metadata = (parsedDefinition as { metadata?: { rulesets?: unknown } } | null)?.metadata
  return Array.isArray(metadata?.rulesets)
    ? metadata.rulesets.filter((r): r is string => typeof r === 'string')
    : []
}

/** Explains why the labels can't be saved, or null when they are fine. */
export function rulesetLabelsError(labels: readonly string[]): string | null {
  if (labels.length > MAX_RULESET_LABELS) return `Use at most ${MAX_RULESET_LABELS} ruleset labels.`
  if (labels.some((l) => l.length > MAX_RULESET_LABEL_LENGTH))
    return `Each ruleset label must be ${MAX_RULESET_LABEL_LENGTH} characters or fewer.`
  return null
}

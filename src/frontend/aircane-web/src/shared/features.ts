import { reactive } from 'vue'

/**
 * Feature flags: parts of the app that are built but switched off, so a feature can be hidden without deleting
 * it. Each flag is read once, at build time, from a `VITE_ENABLE_*` environment variable (see `.env.example`)
 * and is off unless the variable says otherwise.
 *
 * `features` is reactive, so templates and computed values that read it update if a test flips a flag. Nothing
 * in the app changes a flag at run time.
 *
 * - `characters`: character management and creation (the Characters page, importing, and assigning characters
 *   to players). Off: the UI is hidden and `/characters` redirects to the dashboard; the code, backend
 *   endpoints and stored data are untouched. It is a stretch goal (docs/backlog.md).
 */

const ENABLED_VALUES = new Set(['true', '1', 'yes', 'on'])

/** Reads a flag's environment value: only "true", "1", "yes" or "on" (any case) turn it on; anything else is off. */
export function parseFlag(value: unknown): boolean {
  return typeof value === 'string' && ENABLED_VALUES.has(value.trim().toLowerCase())
}

export const features = reactive({
  characters: parseFlag(import.meta.env.VITE_ENABLE_CHARACTERS),
})

export type FeatureName = keyof typeof features

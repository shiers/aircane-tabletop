/**
 * Feature flags for the end-to-end tests. The frontend dev server that Playwright starts reads
 * `VITE_ENABLE_CHARACTERS` from this process's environment, so one variable decides both what the app shows and
 * what the specs expect: `VITE_ENABLE_CHARACTERS=true npx playwright test` runs with the Characters UI on.
 * See docs/setup/feature-flags.md.
 */
const ENABLED_VALUES = ['true', '1', 'yes', 'on']

export const charactersEnabled = ENABLED_VALUES.includes((process.env.VITE_ENABLE_CHARACTERS ?? '').trim().toLowerCase())

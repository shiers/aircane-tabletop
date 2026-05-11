import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for Aircane Tabletop E2E tests.
 *
 * These tests run against the Vite dev server on localhost:5173.
 * Start the frontend before running: `npm run dev` in src/frontend/aircane-web/
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'npm run dev',
    cwd: '../../../src/frontend/aircane-web',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 30_000,
  },
})

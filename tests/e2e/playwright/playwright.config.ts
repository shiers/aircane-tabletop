import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright configuration for Aircane Tabletop E2E tests.
 *
 * These tests run against the full stack:
 * - Backend: ASP.NET Core on localhost:5000
 * - Frontend: Vite dev server on localhost:5173 (proxies /api and /hubs to backend)
 * - Database: PostgreSQL on localhost:5432 (must be running via Docker)
 *
 * Prerequisites:
 *   docker compose up postgres -d
 *   dotnet ef database update --project src/backend/Aircane.Infrastructure --startup-project src/backend/Aircane.Api
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: [['html'], ['list']],
  timeout: 30_000,
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: [
    {
      command: 'dotnet run --no-build',
      cwd: '../../../src/backend/Aircane.Api',
      url: 'http://localhost:5000/health',
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: 'npm run dev',
      cwd: '../../../src/frontend/aircane-web',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
})

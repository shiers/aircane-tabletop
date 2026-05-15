import { test, expect } from '@playwright/test'

test.describe('Session Join Page', () => {
  test('renders the join session page with title', async ({ page }) => {
    // Use a fake session ID - the page should still render its structure
    await page.goto('/join/test-session-123')

    // The page should show the Aircane Tabletop branding
    await expect(page.locator('text=Aircane Tabletop')).toBeVisible()
  })

  test('shows loading or session content', async ({ page }) => {
    await page.goto('/join/test-session-123')

    // The page should either show a loading state or session info
    // Since there's no backend, it will likely show loading or an error
    const pageContent = page.locator('main')
    await expect(pageContent).toBeVisible()
  })
})

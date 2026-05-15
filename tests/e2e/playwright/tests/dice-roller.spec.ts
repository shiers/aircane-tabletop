import { test, expect } from '@playwright/test'

test.describe('Sessions / Dice Roller Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/sessions')
  })

  test('renders the sessions page', async ({ page }) => {
    // The /sessions route is the sessions list (dice roller is within active sessions)
    // Just verify the page loads without errors
    await expect(page.locator('h1').first()).toBeVisible()
  })

  test('no unhandled errors on page load', async ({ page }) => {
    const pageErrors: string[] = []
    page.on('pageerror', (err) => pageErrors.push(err.message))

    await page.goto('/sessions')
    await page.waitForTimeout(2_000)

    // Filter out expected API errors (404 from session endpoints when no session exists)
    const unexpectedErrors = pageErrors.filter(
      (e) => !e.includes('404') && !e.includes('Request failed'),
    )
    expect(unexpectedErrors).toHaveLength(0)
  })
})

import { test, expect } from '@playwright/test'

test.describe('Dice Roller Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/sessions')
  })

  test('renders the dice roller interface', async ({ page }) => {
    // Look for dice-related UI elements
    const diceInput = page.getByPlaceholder(/dice|expression|roll|d20/i)
      .or(page.getByLabel(/dice|expression|formula/i))

    await expect(diceInput.first()).toBeVisible({ timeout: 5_000 })
  })

  test('no unhandled errors on page load', async ({ page }) => {
    const pageErrors: string[] = []
    page.on('pageerror', (err) => pageErrors.push(err.message))

    await page.goto('/sessions')
    await page.waitForTimeout(2_000)

    expect(pageErrors).toHaveLength(0)
  })
})

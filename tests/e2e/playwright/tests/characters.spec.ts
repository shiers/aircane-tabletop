import { test, expect } from '@playwright/test'

test.describe('Characters Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/characters')
  })

  test('renders the characters page with heading', async ({ page }) => {
    await expect(
      page.locator('h1', { hasText: /character/i }),
    ).toBeVisible()
  })

  test('shows empty state or character list', async ({ page }) => {
    // Wait for API response
    await page.waitForResponse(
      (resp) => resp.url().includes('/api/characters') && resp.ok(),
      { timeout: 10_000 },
    )
    await page.waitForTimeout(500)

    // Should show either empty state or character cards
    const emptyState = page.getByText(/no characters/i)
    const characterCards = page.locator('[aria-label*="character" i]')
    const createButton = page.getByRole('button', { name: /create|add|new/i })

    const isEmpty = await emptyState.isVisible().catch(() => false)
    const hasChars = (await characterCards.count()) > 0
    const hasCreate = await createButton.isVisible().catch(() => false)

    // At least one of these should be true
    expect(isEmpty || hasChars || hasCreate).toBe(true)
  })

  test('create character button or link is available', async ({ page }) => {
    // Look for any create/add/new character action
    const createAction = page.getByRole('button', { name: /create|add|new/i })
      .or(page.getByRole('link', { name: /create|add|new/i }))

    await expect(createAction.first()).toBeVisible({ timeout: 5_000 })
  })

  test('no unhandled errors on page load', async ({ page }) => {
    const pageErrors: string[] = []
    page.on('pageerror', (err) => pageErrors.push(err.message))

    await page.goto('/characters')
    await page.waitForTimeout(2_000)

    expect(pageErrors).toHaveLength(0)
  })
})

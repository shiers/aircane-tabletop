import { test, expect } from '@playwright/test'

test.describe('Characters Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/characters')
  })

  test('renders the characters page with heading', async ({ page }) => {
    await expect(
      page.locator('h1', { hasText: /characters/i }),
    ).toBeVisible()
  })

  test('action buttons are visible (New Character, Import PDF, Import JSON)', async ({ page }) => {
    await expect(page.getByRole('button', { name: /new character/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /import pdf/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /import json/i })).toBeVisible()
  })

  test('clicking New Character shows the character form', async ({ page }) => {
    await page.getByRole('button', { name: /new character/i }).click()

    await expect(
      page.getByRole('heading', { name: /new character/i }),
    ).toBeVisible()
  })

  test('no unhandled errors on page load', async ({ page }) => {
    const pageErrors: string[] = []
    page.on('pageerror', (err) => pageErrors.push(err.message))

    await page.goto('/characters')
    await page.waitForTimeout(2_000)

    expect(pageErrors).toHaveLength(0)
  })
})

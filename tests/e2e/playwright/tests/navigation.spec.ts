import { test, expect } from '@playwright/test'

test.describe('App Navigation', () => {
  test('sidebar contains all main navigation links', async ({ page }) => {
    await page.goto('/')

    // Check sidebar navigation items
    await expect(page.getByRole('link', { name: /dashboard/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /campaigns/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /library/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /characters/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /rules lookup/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /ai dm/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /adventure/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /dice/i })).toBeVisible()
  })

  test('clicking Library navigates to /library', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /library/i }).click()
    await expect(page).toHaveURL('/library')
  })

  test('clicking Campaigns navigates to /campaigns', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /campaigns/i }).click()
    await expect(page).toHaveURL('/campaigns')
  })

  test('clicking Characters navigates to /characters', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /characters/i }).click()
    await expect(page).toHaveURL('/characters')
  })

  test('clicking Rules Lookup navigates correctly', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /rules lookup/i }).click()
    await expect(page).toHaveURL(/rules/)
  })

  test('all pages load without JavaScript errors', async ({ page }) => {
    const routes = ['/', '/library', '/campaigns', '/characters', '/sessions']
    const errors: { route: string; error: string }[] = []

    for (const route of routes) {
      page.on('pageerror', (err) => {
        errors.push({ route, error: err.message })
      })

      await page.goto(route)
      await page.waitForTimeout(1_000)
    }

    if (errors.length > 0) {
      console.log('Page errors found:', errors)
    }
    expect(errors).toHaveLength(0)
  })

  test('health status indicator is visible on all pages', async ({ page }) => {
    const routes = ['/', '/library', '/campaigns', '/characters']

    for (const route of routes) {
      await page.goto(route)
      await expect(page.getByText(/Backend:/i)).toBeVisible({ timeout: 10_000 })
    }
  })
})

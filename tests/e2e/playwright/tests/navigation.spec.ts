import { test, expect } from '@playwright/test'

test.describe('App Navigation', () => {
  test('sidebar contains all main navigation links', async ({ page }) => {
    await page.goto('/')

    const nav = page.locator('nav[aria-label="Main navigation"]')
    await expect(nav).toBeVisible()

    // Check all sidebar navigation items by their text
    await expect(nav.getByText('Dashboard')).toBeVisible()
    await expect(nav.getByText('Campaigns')).toBeVisible()
    await expect(nav.getByText('Library')).toBeVisible()
    await expect(nav.getByText('Characters')).toBeVisible()
    await expect(nav.getByText('Rules Lookup')).toBeVisible()
    await expect(nav.getByText('AI DM')).toBeVisible()
    await expect(nav.getByText('Adventure Forge')).toBeVisible()
    await expect(nav.getByText('Dice Roller')).toBeVisible()
  })

  test('clicking Library navigates to /library', async ({ page }) => {
    await page.goto('/')
    const nav = page.locator('nav[aria-label="Main navigation"]')
    await nav.getByText('Library').click()
    await expect(page).toHaveURL('/library')
  })

  test('clicking Campaigns navigates to /campaigns', async ({ page }) => {
    await page.goto('/')
    const nav = page.locator('nav[aria-label="Main navigation"]')
    await nav.getByText('Campaigns').click()
    await expect(page).toHaveURL('/campaigns')
  })

  test('clicking Characters navigates to /characters', async ({ page }) => {
    await page.goto('/')
    const nav = page.locator('nav[aria-label="Main navigation"]')
    await nav.getByText('Characters').click()
    await expect(page).toHaveURL('/characters')
  })

  test('clicking Rules Lookup navigates to /rules-lookup', async ({ page }) => {
    await page.goto('/')
    const nav = page.locator('nav[aria-label="Main navigation"]')
    await nav.getByText('Rules Lookup').click()
    await expect(page).toHaveURL('/rules-lookup')
  })

  test('clicking Adventure Forge navigates to /adventures/generate', async ({ page }) => {
    await page.goto('/')
    const nav = page.locator('nav[aria-label="Main navigation"]')
    await nav.getByText('Adventure Forge').click()
    await expect(page).toHaveURL('/adventures/generate')
  })

  test('all pages load without JavaScript errors', async ({ page }) => {
    const routes = ['/', '/library', '/campaigns', '/characters', '/sessions', '/rules-lookup', '/settings/ai', '/adventures/generate']
    const errors: { route: string; error: string }[] = []

    for (const route of routes) {
      const pageErrorHandler = (err: Error) => {
        errors.push({ route, error: err.message })
      }
      page.on('pageerror', pageErrorHandler)

      await page.goto(route)
      await page.waitForTimeout(1_000)

      page.off('pageerror', pageErrorHandler)
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

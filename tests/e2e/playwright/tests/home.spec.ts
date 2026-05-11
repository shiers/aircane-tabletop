import { test, expect } from '@playwright/test'

test.describe('Home Page', () => {
  test('renders the app title and hero section', async ({ page }) => {
    await page.goto('/')

    // App title in header
    await expect(page.locator('header')).toContainText('Aircane Tabletop')

    // Hero heading
    await expect(
      page.getByRole('heading', { name: /your ai-assisted tabletop rpg/i }),
    ).toBeVisible()

    // Hero description
    await expect(page.locator('text=Import rules, run campaigns')).toBeVisible()
  })

  test('displays feature cards', async ({ page }) => {
    await page.goto('/')

    // Check that all 6 feature cards render
    await expect(page.getByText('Document Library')).toBeVisible()
    await expect(page.getByText('Campaign Management')).toBeVisible()
    await expect(page.getByText('Character Sheets')).toBeVisible()
    await expect(page.getByText('Dice Roller')).toBeVisible()
    await expect(page.getByText('AI DM Runtime')).toBeVisible()
    await expect(page.getByText('Adventure Generation')).toBeVisible()
  })

  test('navigation links are present', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('link', { name: /start a campaign/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /import documents/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /rules lookup/i })).toBeVisible()
  })

  test('navigates to campaigns page', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /start a campaign/i }).click()
    await expect(page).toHaveURL('/campaigns')
  })
})

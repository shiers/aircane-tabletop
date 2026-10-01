import { test, expect } from '@playwright/test'

test.describe('Home Page', () => {
  test('renders the welcome heading and description', async ({ page }) => {
    await page.goto('/')

    // Welcome heading
    await expect(
      page.getByRole('heading', { name: /welcome back, dungeon master/i }),
    ).toBeVisible()

    // Description text
    await expect(page.getByText(/your ai-assisted adventure begins here/i)).toBeVisible()
  })

  test('displays feature cards', async ({ page }) => {
    await page.goto('/')

    // Check that all 6 feature cards render
    await expect(page.getByRole('heading', { name: 'Document Library' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Campaign Management' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Character Sheets' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Dice Roller' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'AI DM Runtime' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Adventure Generation' })).toBeVisible()
  })

  test('quick action links are present', async ({ page }) => {
    await page.goto('/')

    await expect(page.getByRole('link', { name: /start ai dm/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /import documents/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /create character/i })).toBeVisible()
    await expect(page.getByRole('link', { name: /roll dice/i })).toBeVisible()
  })

  test('new campaign button navigates to campaigns page', async ({ page }) => {
    await page.goto('/')
    await page.getByRole('link', { name: /new campaign/i }).click()
    await expect(page).toHaveURL('/campaigns')
  })

  test('feature card links navigate to correct pages', async ({ page }) => {
    await page.goto('/')

    // Click the Document Library card
    await page.getByRole('link', { name: /document library/i }).click()
    await expect(page).toHaveURL('/library')
  })
})

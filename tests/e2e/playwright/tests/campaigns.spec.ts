import { test, expect } from '@playwright/test'

test.describe('Campaigns Page', () => {
  test('renders the campaigns page with create button', async ({ page }) => {
    await page.goto('/campaigns')

    // Page should have the Campaigns heading
    await expect(page.getByRole('heading', { name: 'Campaigns', exact: true })).toBeVisible()

    // Should have a button to create a new campaign
    const createButton = page.getByRole('button', { name: /new campaign/i })
    await expect(createButton).toBeVisible()
  })

  test('opens campaign creation form', async ({ page }) => {
    await page.goto('/campaigns')

    // Click the create button
    const createButton = page.getByRole('button', { name: /new campaign/i })
    await createButton.click()

    // Form heading should appear
    await expect(page.getByRole('heading', { name: /new campaign/i })).toBeVisible()
  })
})

import { test, expect } from '@playwright/test'

test.describe('Adventure Generation Page', () => {
  test('renders the adventure generation form with all fields', async ({ page }) => {
    await page.goto('/adventures/generate')

    // Page heading
    await expect(page.getByRole('heading', { name: /generate adventure/i })).toBeVisible()

    // Mode selector (Solo/Group radio buttons)
    await expect(page.getByText('Solo')).toBeVisible()
    await expect(page.getByText('Group')).toBeVisible()

    // Core fields
    await expect(page.getByLabel(/ruleset/i)).toBeVisible()
    await expect(page.getByLabel(/tone/i)).toBeVisible()
    await expect(page.getByLabel(/length/i)).toBeVisible()
    await expect(page.getByLabel(/difficulty/i)).toBeVisible()

    // Content ratios
    await expect(page.getByLabel(/combat/i)).toBeVisible()
    await expect(page.getByLabel(/exploration/i)).toBeVisible()
    await expect(page.getByLabel(/roleplay/i)).toBeVisible()

    // Submit button
    await expect(page.getByRole('button', { name: /generate adventure/i })).toBeVisible()
  })

  test('switches between Solo and Group mode', async ({ page }) => {
    await page.goto('/adventures/generate')

    // Default is Solo - party size should not be visible
    await expect(page.getByLabel(/party size/i)).not.toBeVisible()

    // Switch to Group mode
    await page.getByText('Group').click()

    // Party size should now be visible
    await expect(page.getByLabel(/party size/i)).toBeVisible()
  })

  test('level field is present and accepts input', async ({ page }) => {
    await page.goto('/adventures/generate')

    const levelInput = page.getByLabel(/level/i)
    await expect(levelInput).toBeVisible()
    await levelInput.fill('5')
    await expect(levelInput).toHaveValue('5')
  })
})

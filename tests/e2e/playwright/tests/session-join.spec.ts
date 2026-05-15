import { test, expect } from '@playwright/test'

test.describe('Session Join Page', () => {
  test('renders the join session page with heading', async ({ page }) => {
    // Navigate to a join URL with a fake session ID
    await page.goto('/join/00000000-0000-0000-0000-000000000000')

    // The page should show the Join Session heading
    await expect(
      page.getByRole('heading', { name: /join session/i }),
    ).toBeVisible()
  })

  test('shows loading state or error for invalid session', async ({ page }) => {
    await page.goto('/join/00000000-0000-0000-0000-000000000000')

    // Should show either loading, error, or the join form
    const loading = page.getByText(/loading session/i)
    const error = page.getByText(/session not found/i)
    const joinForm = page.locator('form')

    // Wait for one of these to appear
    await expect(
      loading.or(error).or(joinForm),
    ).toBeVisible({ timeout: 10_000 })
  })

  test('error state shows back to home link', async ({ page }) => {
    await page.goto('/join/00000000-0000-0000-0000-000000000000')

    // Wait for the error state (invalid session ID will fail)
    const error = page.getByText(/session not found/i)
    await expect(error).toBeVisible({ timeout: 10_000 })

    // Back to home link should be present
    await expect(page.getByRole('link', { name: /back to home/i })).toBeVisible()
  })
})

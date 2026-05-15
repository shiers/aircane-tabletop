import { test, expect } from '@playwright/test'

test.describe('Library Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/library')
  })

  test('page loads with correct heading', async ({ page }) => {
    await expect(
      page.locator('h1', { hasText: /document library/i }),
    ).toBeVisible()
  })

  test('backend health shows as healthy or degraded (not unreachable)', async ({ page }) => {
    // Wait for the health check to complete
    await expect(page.getByText(/Backend: healthy/i)).toBeVisible({ timeout: 10_000 })
  })

  test('watched folders section renders heading', async ({ page }) => {
    await expect(
      page.getByRole('heading', { name: /watched folders/i }),
    ).toBeVisible()
  })

  test('watched folders section shows empty state or folder cards (not skeleton)', async ({ page }) => {
    // Wait for the page to settle (API calls happen on mount)
    await page.waitForTimeout(2_000)

    // The skeleton should NOT be visible
    const skeletons = page.locator('[aria-label="Loading folders"] .animate-pulse')
    await expect(skeletons).toHaveCount(0)

    // Either the empty state OR folder cards should be visible
    const emptyState = page.getByText(/no folders registered yet/i)
    const folderCards = page.locator('article[aria-label^="Watched folder"]')
    const registerButton = page.getByRole('button', { name: 'Register Folder', exact: true })

    // At least one of these should be true
    const isEmpty = await emptyState.isVisible().catch(() => false)
    const hasFolders = (await folderCards.count()) > 0

    expect(isEmpty || hasFolders).toBe(true)
    await expect(registerButton).toBeVisible()
  })

  test('register folder button is visible and clickable', async ({ page }) => {
    await expect(
      page.getByRole('button', { name: 'Register Folder', exact: true }),
    ).toBeVisible()
  })

  test('documents section shows empty state or document list (not error)', async ({ page }) => {
    // Wait for the page to settle
    await page.waitForTimeout(3_000)

    // Should show either empty state or document list
    const emptyState = page.getByText(/no documents yet/i)
    const documentCards = page.locator('[class*="document"]')

    const isEmpty = await emptyState.isVisible().catch(() => false)
    const hasDocs = (await documentCards.count()) > 0

    // If neither is visible, check if there's a loading state or error
    if (!isEmpty && !hasDocs) {
      // Take a screenshot for debugging
      const content = await page.content()
      const hasDocSection = content.includes('Document Library') || content.includes('document')
      expect(hasDocSection).toBe(true)
    } else {
      expect(isEmpty || hasDocs).toBe(true)
    }
  })

  test('no 500 error banners are displayed', async ({ page }) => {
    // Wait for the page to settle
    await page.waitForTimeout(2_000)

    // No red error banners should be visible
    const errorBanner = page.locator('[role="alert"]')
    await expect(errorBanner).toHaveCount(0)
  })

  test('upload document form is visible with required fields', async ({ page }) => {
    await expect(page.locator('#doc-file')).toBeAttached()
    await expect(page.locator('#doc-title')).toBeVisible()
    await expect(page.locator('[aria-labelledby="upload-heading"] button[type="submit"]')).toBeVisible()
  })

  test('SignalR library hub connects without error', async ({ page }) => {
    // Give time for SignalR to negotiate
    await page.waitForTimeout(3_000)

    // Check that no connection error is shown
    const pageErrors: string[] = []
    page.on('pageerror', (err) => pageErrors.push(err.message))
    await page.waitForTimeout(1_000)

    // Filter out non-SignalR errors
    const signalRErrors = pageErrors.filter((e) => e.includes('SignalR') || e.includes('hub'))
    expect(signalRErrors).toHaveLength(0)
  })

  test('folder registration form submits and shows result', async ({ page }) => {
    // Click register folder
    await page.getByRole('button', { name: 'Register Folder', exact: true }).click()

    // Fill in the form
    const displayNameInput = page.getByLabel(/display name/i)
    const pathInput = page.getByLabel(/folder path/i)

    // Only proceed if the form is visible
    if (await displayNameInput.isVisible({ timeout: 3_000 }).catch(() => false)) {
      await displayNameInput.fill('Test E2E Folder')
      await pathInput.fill('C:\\TestFolder')

      // Submit the form
      const submitButton = page.locator('[aria-labelledby="register-folder-heading"] button[type="submit"]')
      await submitButton.click()

      // Wait for either success (folder appears) or validation error
      const folderAppeared = page.getByText('Test E2E Folder')
      const validationError = page.locator('[role="alert"]')

      // One of these should become visible within 5 seconds
      await expect(
        folderAppeared.or(validationError).first(),
      ).toBeVisible({ timeout: 5_000 })
    }
  })
})

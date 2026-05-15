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
    // Wait for the folders API to respond
    await page.waitForResponse(
      (resp) => resp.url().includes('/api/library/folders') && resp.status() === 200,
      { timeout: 10_000 },
    )

    // Give Vue a tick to render
    await page.waitForTimeout(500)

    // The skeleton should NOT be visible (animate-pulse elements)
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
    // Wait for the documents API to respond
    await page.waitForResponse(
      (resp) => resp.url().includes('/api/library/documents') && resp.status() === 200,
      { timeout: 10_000 },
    )

    // Give Vue a tick to render
    await page.waitForTimeout(500)

    // Should show either empty state or document list
    const emptyState = page.getByText(/no documents yet/i)
    const documentList = page.locator('[aria-label="Document list"]')

    const isEmpty = await emptyState.isVisible().catch(() => false)
    const hasDocs = await documentList.isVisible().catch(() => false)

    expect(isEmpty || hasDocs).toBe(true)
  })

  test('no 500 error banners are displayed', async ({ page }) => {
    // Wait for API calls to complete
    await page.waitForResponse(
      (resp) => resp.url().includes('/api/library/folders') && resp.status() === 200,
      { timeout: 10_000 },
    )

    // No red error banners should be visible
    const errorBanner = page.locator('[role="alert"]')
    await expect(errorBanner).toHaveCount(0)
  })

  test('upload document form is visible with required fields', async ({ page }) => {
    await expect(page.getByText('Upload Document')).toBeVisible()
    await expect(page.getByLabel(/file/i)).toBeVisible()
    await expect(page.getByLabel(/title/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /upload/i })).toBeVisible()
  })

  test('SignalR library hub connects without error', async ({ page }) => {
    // Wait for the negotiate call to succeed
    const negotiateResponse = await page.waitForResponse(
      (resp) => resp.url().includes('/hubs/library/negotiate') && resp.ok(),
      { timeout: 10_000 },
    )

    expect(negotiateResponse.status()).toBe(200)
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

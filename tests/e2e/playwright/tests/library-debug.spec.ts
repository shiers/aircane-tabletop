import { test, expect } from '@playwright/test'

test.describe('Library Page Debug', () => {
  test('diagnose folders loading state', async ({ page }) => {
    // Capture console errors
    const consoleErrors: string[] = []
    const consoleWarnings: string[] = []
    page.on('console', (msg) => {
      if (msg.type() === 'error') consoleErrors.push(msg.text())
      if (msg.type() === 'warning') consoleWarnings.push(msg.text())
    })

    // Capture uncaught exceptions
    const pageErrors: string[] = []
    page.on('pageerror', (err) => {
      pageErrors.push(err.message)
    })

    // Intercept the folders API call
    const foldersPromise = page.waitForResponse(
      (resp) => resp.url().includes('/api/library/folders'),
      { timeout: 15_000 },
    )

    await page.goto('/library')

    // Wait for the folders API to respond
    const foldersResponse = await foldersPromise
    const foldersData = await foldersResponse.json()

    console.log('Folders API status:', foldersResponse.status())
    console.log('Folders API data:', JSON.stringify(foldersData))

    // Wait for Vue to process and any errors to surface
    await page.waitForTimeout(3_000)

    console.log('Console errors:', consoleErrors)
    console.log('Console warnings:', consoleWarnings)
    console.log('Page errors:', pageErrors)

    // Check what's actually in the DOM
    const skeletonCount = await page.locator('.animate-pulse').count()
    const folderCards = await page.locator('article[aria-label^="Watched folder"]').count()
    const emptyState = await page.getByText(/no folders registered yet/i).isVisible().catch(() => false)

    console.log('DOM state:')
    console.log('  Skeleton elements:', skeletonCount)
    console.log('  Folder cards:', folderCards)
    console.log('  Empty state visible:', emptyState)

    // Check all network requests that failed
    const failedRequests: string[] = []
    page.on('requestfailed', (req) => {
      failedRequests.push(`${req.method()} ${req.url()} - ${req.failure()?.errorText}`)
    })

    // Take a screenshot for reference
    await page.screenshot({ path: 'test-results/library-debug.png', fullPage: true })

    // The actual assertion - folders should render
    if (foldersData.length > 0) {
      expect(folderCards).toBeGreaterThan(0)
    } else {
      expect(emptyState).toBe(true)
    }
  })
})

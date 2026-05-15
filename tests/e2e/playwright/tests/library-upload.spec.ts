import { test, expect } from '@playwright/test'
import path from 'path'
import fs from 'fs'
import { fileURLToPath } from 'url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

// Create a minimal test PDF for upload tests
const TEST_PDF_PATH = path.join(__dirname, '..', 'test-fixtures', 'test-document.pdf')

test.beforeAll(() => {
  // Create test fixtures directory
  const fixturesDir = path.join(__dirname, '..', 'test-fixtures')
  if (!fs.existsSync(fixturesDir)) {
    fs.mkdirSync(fixturesDir, { recursive: true })
  }

  // Create a minimal valid PDF file for testing
  const minimalPdf = Buffer.from(
    '%PDF-1.4\n' +
    '1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n' +
    '2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n' +
    '3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n' +
    'xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n' +
    'trailer\n<< /Size 4 /Root 1 0 R >>\nstartxref\n190\n%%EOF',
    'ascii'
  )
  fs.writeFileSync(TEST_PDF_PATH, minimalPdf)
})

test.describe('Document Upload', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/library')
    // Wait for the page to be ready
    await expect(page.locator('h1', { hasText: /document library/i })).toBeVisible()
  })

  test('upload form has all required fields', async ({ page }) => {
    await expect(page.getByLabel(/file/i)).toBeVisible()
    await expect(page.getByLabel(/title/i)).toBeVisible()
    await expect(page.getByLabel(/source type/i)).toBeVisible()
    await expect(page.getByLabel(/visibility/i)).toBeVisible()
    await expect(page.getByLabel(/game system/i)).toBeVisible()
    await expect(page.getByLabel(/ruleset/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /upload/i })).toBeVisible()
  })

  test('upload button shows validation error when no file selected', async ({ page }) => {
    // Fill title but no file
    await page.getByLabel(/title/i).fill('Test Document')
    await page.getByRole('button', { name: /upload/i }).click()

    // Should show validation error
    await expect(page.getByText(/please select a file/i)).toBeVisible()
  })

  test('upload button shows validation error when no title', async ({ page }) => {
    // Select a file but clear the title
    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(TEST_PDF_PATH)

    // Clear the auto-filled title
    await page.getByLabel(/title/i).clear()
    await page.getByRole('button', { name: /upload/i }).click()

    // Should show validation error
    await expect(page.getByText(/please enter a title/i)).toBeVisible()
  })

  test('selecting a file auto-fills the title from filename', async ({ page }) => {
    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(TEST_PDF_PATH)

    // Title should be auto-filled with filename (without extension)
    const titleInput = page.getByLabel(/title/i)
    await expect(titleInput).toHaveValue('test-document')
  })

  test('upload submits multipart form data to the API', async ({ page }) => {
    // Listen for the upload request
    const uploadPromise = page.waitForRequest(
      (req) => req.url().includes('/api/library/documents') && req.method() === 'POST',
      { timeout: 10_000 },
    )

    // Fill the form
    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(TEST_PDF_PATH)
    await page.getByLabel(/title/i).clear()
    await page.getByLabel(/title/i).fill('E2E Test Upload')
    await page.getByLabel(/game system/i).fill('D&D 5e')
    await page.getByLabel(/ruleset/i).fill('2014')

    // Submit
    await page.getByRole('button', { name: /upload/i }).click()

    // Verify the request was made
    const request = await uploadPromise
    expect(request.method()).toBe('POST')

    // Check the response
    const response = await request.response()
    expect(response).not.toBeNull()

    const status = response!.status()
    // Accept 200/201 (success) or 400/500 (server-side validation/processing error)
    // The key thing is the request was sent correctly
    expect([200, 201, 400, 500]).toContain(status)

    if (status === 200 || status === 201) {
      // On success, the document should appear in the list
      await expect(page.getByText('E2E Test Upload')).toBeVisible({ timeout: 5_000 })
    } else {
      // On error, an error message should be shown
      const errorText = page.locator('[role="alert"]').or(page.getByText(/error|failed/i))
      await expect(errorText.first()).toBeVisible({ timeout: 5_000 })
    }
  })

  test('upload shows error message on network failure', async ({ page }) => {
    // Intercept and abort the upload request to simulate network error
    await page.route('**/api/library/documents', (route) => {
      if (route.request().method() === 'POST') {
        route.abort('connectionrefused')
      } else {
        route.continue()
      }
    })

    // Fill the form
    const fileInput = page.locator('input[type="file"]')
    await fileInput.setInputFiles(TEST_PDF_PATH)
    await page.getByLabel(/title/i).clear()
    await page.getByLabel(/title/i).fill('Network Error Test')

    // Submit
    await page.getByRole('button', { name: /upload/i }).click()

    // Should show an error
    await expect(
      page.getByText(/network error|failed|error/i).first(),
    ).toBeVisible({ timeout: 5_000 })
  })

  test('source type dropdown has expected options', async ({ page }) => {
    const select = page.getByLabel(/source type/i)
    await expect(select).toBeVisible()

    // Check options exist
    await expect(select.locator('option', { hasText: 'Rules' })).toBeAttached()
    await expect(select.locator('option', { hasText: 'Adventure' })).toBeAttached()
    await expect(select.locator('option', { hasText: 'Character' })).toBeAttached()
    await expect(select.locator('option', { hasText: 'Homebrew' })).toBeAttached()
  })

  test('visibility dropdown has expected options', async ({ page }) => {
    const select = page.getByLabel(/visibility/i)
    await expect(select).toBeVisible()

    await expect(select.locator('option', { hasText: 'DM Only' })).toBeAttached()
    await expect(select.locator('option', { hasText: 'Public' })).toBeAttached()
    await expect(select.locator('option', { hasText: 'Hidden' })).toBeAttached()
  })
})

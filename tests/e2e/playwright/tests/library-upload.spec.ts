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
    await expect(page.locator('#doc-file')).toBeAttached()
    await expect(page.locator('#doc-title')).toBeVisible()
    await expect(page.locator('#doc-source-type')).toBeVisible()
    await expect(page.locator('#doc-visibility')).toBeVisible()
    await expect(page.locator('#doc-game-system')).toBeVisible()
    await expect(page.locator('#doc-ruleset')).toBeVisible()
    await expect(page.locator('[aria-labelledby="upload-heading"] button[type="submit"]')).toBeVisible()
  })

  test('upload button shows validation error when no file selected', async ({ page }) => {
    // Fill title but no file
    await page.locator('#doc-title').fill('Test Document')

    // Click the upload submit button
    await page.locator('[aria-labelledby="upload-heading"] button[type="submit"]').click()

    // Should show validation error
    await expect(page.getByText(/please select a file/i)).toBeVisible()
  })

  test('upload button shows validation error when no title', async ({ page }) => {
    // Select a file but clear the title
    const fileInput = page.locator('#doc-file')
    await fileInput.setInputFiles(TEST_PDF_PATH)

    // Clear the auto-filled title
    await page.locator('#doc-title').clear()

    // Click the upload submit button (scoped to the upload form section)
    await page.locator('[aria-labelledby="upload-heading"] button[type="submit"]').click()

    // Should show validation error
    await expect(page.getByText(/please enter a title/i)).toBeVisible()
  })

  test('selecting a file auto-fills the title from filename', async ({ page }) => {
    const fileInput = page.locator('#doc-file')
    await fileInput.setInputFiles(TEST_PDF_PATH)

    // Title should be auto-filled with filename (without extension)
    const titleInput = page.locator('#doc-title')
    await expect(titleInput).toHaveValue('test-document')
  })

  test('upload submits multipart form data to the API', async ({ page }) => {
    // Fill the form
    const fileInput = page.locator('#doc-file')
    await fileInput.setInputFiles(TEST_PDF_PATH)
    await page.locator('#doc-title').clear()
    await page.locator('#doc-title').fill('E2E Test Upload')
    await page.locator('#doc-game-system').fill('D&D 5e')
    await page.locator('#doc-ruleset').fill('2014')

    // Listen for the upload request BEFORE clicking submit
    const uploadPromise = page.waitForResponse(
      (resp) => resp.url().includes('/api/library/documents') && resp.request().method() === 'POST',
      { timeout: 15_000 },
    )

    // Submit
    await page.locator('[aria-labelledby="upload-heading"] button[type="submit"]').click()

    // Verify the request was made and got a response
    const response = await uploadPromise
    const status = response.status()

    // The upload should succeed (200/201) or fail with a validation error (400)
    expect([200, 201, 400]).toContain(status)
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
    const fileInput = page.locator('#doc-file')
    await fileInput.setInputFiles(TEST_PDF_PATH)
    await page.locator('#doc-title').clear()
    await page.locator('#doc-title').fill('Network Error Test')

    // Submit using the scoped button
    await page.locator('[aria-labelledby="upload-heading"] button[type="submit"]').click()

    // Should show an error (either in the form or as a banner)
    await expect(
      page.locator('[role="alert"]').or(page.getByText(/error|failed/i)).first(),
    ).toBeVisible({ timeout: 10_000 })
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

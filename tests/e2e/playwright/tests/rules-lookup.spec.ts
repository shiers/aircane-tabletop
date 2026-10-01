import { test, expect } from '@playwright/test'

test.describe('Rules Lookup Page', () => {
  test('renders the rules lookup form', async ({ page }) => {
    await page.goto('/rules-lookup')

    // Page heading
    await expect(page.getByRole('heading', { name: /rules lookup/i })).toBeVisible()

    // Question textarea
    await expect(page.getByLabel(/your question/i)).toBeVisible()

    // Submit button
    await expect(page.getByRole('button', { name: /ask question/i })).toBeVisible()
  })

  test('accepts input in the question field', async ({ page }) => {
    await page.goto('/rules-lookup')

    const textarea = page.getByLabel(/your question/i)
    await textarea.fill('How does grappling work in D&D 5e?')

    await expect(textarea).toHaveValue('How does grappling work in D&D 5e?')
  })

  test('submit button is disabled when question is empty', async ({ page }) => {
    await page.goto('/rules-lookup')

    const submitButton = page.getByRole('button', { name: /ask question/i })
    await expect(submitButton).toBeDisabled()
  })

  test('submit button is enabled when question has text', async ({ page }) => {
    await page.goto('/rules-lookup')

    await page.getByLabel(/your question/i).fill('What is AC?')

    const submitButton = page.getByRole('button', { name: /ask question/i })
    await expect(submitButton).toBeEnabled()
  })
})

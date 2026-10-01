<script setup lang="ts">
import { ref, computed } from 'vue'
import type { ImportCharacterJsonRequest } from '../api'

// ---------------------------------------------------------------------------
// Props / emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** Optional campaign ID to associate the imported character with. */
  campaignId?: string
  /** Whether the modal is currently visible. */
  open: boolean
}>()

const emit = defineEmits<{
  /** Emitted when the user submits a valid import request. */
  (e: 'import', request: ImportCharacterJsonRequest): void
  /** Emitted when the user closes or cancels the modal. */
  (e: 'close'): void
}>()

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/** The raw JSON text entered by the user (textarea) or loaded from a file. */
const jsonText = ref('')
const gameSystem = ref('D&D 5e')
const ruleset = ref('2014')
const originalFileName = ref<string | null>(null)
const localErrors = ref<string[]>([])
const submitting = ref(false)

const fileInput = ref<HTMLInputElement | null>(null)

const hasContent = computed(() => jsonText.value.trim().length > 0)

// ---------------------------------------------------------------------------
// File input
// ---------------------------------------------------------------------------

function triggerFileInput(): void {
  fileInput.value?.click()
}

function handleFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  originalFileName.value = file.name
  localErrors.value = []

  file
    .text()
    .then((text) => {
      jsonText.value = text
    })
    .catch(() => {
      localErrors.value = ['Failed to read the selected file.']
    })

  // Reset so the same file can be re-selected if needed
  input.value = ''
}

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

function validate(): string[] {
  const errors: string[] = []
  const trimmedJson = jsonText.value.trim()
  if (trimmedJson) {
    try {
      JSON.parse(trimmedJson)
    } catch {
      errors.push('The text is not valid JSON. Please check the content and try again.')
    }
  } else {
    errors.push('JSON content is required. Paste JSON or select a .json file.')
  }
  if (!gameSystem.value.trim()) errors.push('Game system is required.')
  if (!ruleset.value.trim()) errors.push('Ruleset is required.')
  return errors
}

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

async function handleSubmit(): Promise<void> {
  localErrors.value = []

  const validationErrors = validate()
  if (validationErrors.length > 0) {
    localErrors.value = validationErrors
    return
  }

  submitting.value = true
  try {
    const request: ImportCharacterJsonRequest = {
      canonicalJson: jsonText.value.trim(),
      gameSystem: gameSystem.value.trim(),
      ruleset: ruleset.value.trim(),
      campaignId: props.campaignId ?? null,
      originalFileName: originalFileName.value,
    }
    emit('import', request)
  } finally {
    submitting.value = false
  }
}

// ---------------------------------------------------------------------------
// Reset when closed
// ---------------------------------------------------------------------------

function handleClose(): void {
  jsonText.value = ''
  gameSystem.value = 'D&D 5e'
  ruleset.value = '2014'
  originalFileName.value = null
  localErrors.value = []
  emit('close')
}

// Expose for parent to push server-side errors into the modal
function setErrors(errors: string[]): void {
  localErrors.value = errors
  submitting.value = false
}

defineExpose({ setErrors })
</script>

<template>
  <!-- Backdrop -->
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
      aria-hidden="true"
      @click="handleClose"
    >
      <!-- Native dialog for accessibility -->
      <dialog
        open
        class="m-0 w-full max-w-2xl rounded-xl border border-gray-700 bg-gray-900 p-0 shadow-2xl"
        aria-labelledby="import-modal-title"
        @click.stop
        @keydown.esc="handleClose"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
          <h2 id="import-modal-title" class="text-lg font-semibold text-white">
            Import Character JSON
          </h2>
          <button
            type="button"
            class="rounded p-1 text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            aria-label="Close import modal"
            @click="handleClose"
          >
            <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path
                d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z"
              />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <form novalidate class="space-y-5 px-6 py-5" @submit.prevent="handleSubmit">

          <!-- File picker -->
          <div>
            <p class="mb-2 text-sm font-medium text-gray-300">
              Select a <code class="rounded bg-gray-800 px-1 py-0.5 text-xs text-aircane-300">.json</code> file or paste JSON below.
            </p>
            <input
              ref="fileInput"
              type="file"
              accept=".json,application/json"
              class="sr-only"
              aria-label="Select JSON file"
              @change="handleFileChange"
            />
            <button
              type="button"
              class="inline-flex items-center gap-2 rounded-lg border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
              @click="triggerFileInput"
            >
              <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path
                  d="M9.25 13.25a.75.75 0 001.5 0V4.636l2.955 3.129a.75.75 0 001.09-1.03l-4.25-4.5a.75.75 0 00-1.09 0l-4.25 4.5a.75.75 0 101.09 1.03L9.25 4.636v8.614z"
                />
                <path
                  d="M3.5 12.75a.75.75 0 00-1.5 0v2.5A2.75 2.75 0 004.75 18h10.5A2.75 2.75 0 0018 15.25v-2.5a.75.75 0 00-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5z"
                />
              </svg>
              Choose File
            </button>
            <span v-if="originalFileName" class="ml-3 text-sm text-gray-400">
              {{ originalFileName }}
            </span>
          </div>

          <!-- JSON textarea -->
          <div>
            <label for="import-json-text" class="mb-1 block text-sm font-medium text-gray-300">
              Character JSON
              <span aria-hidden="true" class="text-red-400">*</span>
            </label>
            <textarea
              id="import-json-text"
              v-model="jsonText"
              rows="10"
              required
              aria-required="true"
              placeholder='{ "identity": { "name": "Aldric Stonehammer" }, "classes": [...], ... }'
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 font-mono text-xs text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              spellcheck="false"
            />
          </div>

          <!-- Game system + Ruleset -->
          <div class="grid gap-4 sm:grid-cols-2">
            <div>
              <label for="import-game-system" class="mb-1 block text-sm font-medium text-gray-300">
                Game System <span aria-hidden="true" class="text-red-400">*</span>
              </label>
              <input
                id="import-game-system"
                v-model="gameSystem"
                type="text"
                required
                aria-required="true"
                placeholder="e.g. D&D 5e"
                class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              />
            </div>
            <div>
              <label for="import-ruleset" class="mb-1 block text-sm font-medium text-gray-300">
                Ruleset <span aria-hidden="true" class="text-red-400">*</span>
              </label>
              <input
                id="import-ruleset"
                v-model="ruleset"
                type="text"
                required
                aria-required="true"
                placeholder="e.g. 2014"
                class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              />
            </div>
          </div>

          <!-- Validation errors -->
          <div
            v-if="localErrors.length > 0"
            role="alert"
            aria-live="polite"
            class="rounded-lg border border-red-800 bg-red-950 px-4 py-3"
          >
            <p class="mb-1 text-sm font-semibold text-red-300">Import failed:</p>
            <ul class="list-inside list-disc space-y-0.5 text-sm text-red-300">
              <li v-for="(err, i) in localErrors" :key="i">{{ err }}</li>
            </ul>
          </div>

          <!-- Actions -->
          <div class="flex items-center justify-end gap-3 border-t border-gray-800 pt-4">
            <button
              type="button"
              class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
              @click="handleClose"
            >
              Cancel
            </button>
            <button
              type="submit"
              :disabled="submitting || !hasContent"
              class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <svg
                v-if="submitting"
                class="h-4 w-4 animate-spin"
                viewBox="0 0 24 24"
                fill="none"
                aria-hidden="true"
              >
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                <path
                  class="opacity-75"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8v8H4z"
                />
              </svg>
              {{ submitting ? 'Importing…' : 'Import Character' }}
            </button>
          </div>
        </form>
      </dialog>
    </div>
  </Teleport>
</template>

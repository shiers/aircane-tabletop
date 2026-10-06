<script setup lang="ts">
import { ref, computed } from 'vue'
import { useCharacterStore } from '../store'
import type { ImportCharacterJsonRequest, SourceImportResponse } from '../api'
import {
  listGameSystems,
} from '@/features/game-systems/api'
import type { GameSystemDefinitionSummary } from '@/features/game-systems/types'
import ImportReviewPanel from './ImportReviewPanel.vue'
import { sourceBadge, SOURCE_OPTIONS } from './sourceBadges'

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
  /** Emitted when an import completes and is confirmed/saved. */
  (e: 'completed'): void
  /** Emitted when the user closes or cancels the modal. */
  (e: 'close'): void
}>()

const store = useCharacterStore()

// ---------------------------------------------------------------------------
// Tabs
// ---------------------------------------------------------------------------

type Tab = 'file' | 'url'
const activeTab = ref<Tab>('file')

const tabs: { id: Tab; label: string }[] = [
  { id: 'file', label: 'Upload File' },
  { id: 'url', label: 'D&D Beyond URL' },
]

function switchTab(tab: Tab): void {
  activeTab.value = tab
  localErrors.value = []
}

// ---------------------------------------------------------------------------
// Shared state
// ---------------------------------------------------------------------------

const localErrors = ref<string[]>([])
const submitting = ref(false)
/** The import response once the backend returns it; drives the review panel. */
const importResponse = ref<SourceImportResponse | null>(null)

// Upload File state
const jsonText = ref('')
const originalFileName = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
/** The source the user chose to override auto-detect ('' = Auto-detect). */
const selectedSource = ref<string>('')
/** The game system the user chose when the backend asks for one. */
const selectedGameSystemId = ref<string>('')
const gameSystems = ref<GameSystemDefinitionSummary[]>([])

// D&D Beyond URL state
const dndbeyondUrl = ref('')

const hasJsonContent = computed(() => jsonText.value.trim().length > 0)

/** The detected source badge shown after a file import attempt. */
const detectedBadge = computed(() =>
  importResponse.value ? sourceBadge(importResponse.value.detectedSource) : null,
)

const needsSourceConfirmation = computed(
  () => importResponse.value?.requiresSourceConfirmation === true,
)

const needsGameSystemSelection = computed(
  () => importResponse.value?.review.requiresGameSystemSelection === true,
)

/** The review panel is shown once we have a response that does not need a confirmation prompt. */
const showReview = computed(
  () =>
    importResponse.value !== null &&
    !needsSourceConfirmation.value &&
    !needsGameSystemSelection.value,
)

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
  importResponse.value = null

  file
    .text()
    .then((text) => {
      jsonText.value = text
    })
    .catch(() => {
      localErrors.value = ['Failed to read the selected file.']
    })

  input.value = ''
}

// ---------------------------------------------------------------------------
// Game system list (lazy)
// ---------------------------------------------------------------------------

async function ensureGameSystems(): Promise<void> {
  if (gameSystems.value.length > 0) return
  try {
    gameSystems.value = await listGameSystems()
  } catch {
    // Non-fatal: the picker simply shows no options.
  }
}

// ---------------------------------------------------------------------------
// Upload File submit (and re-POST on confirmation)
// ---------------------------------------------------------------------------

function buildBaseRequest(): ImportCharacterJsonRequest {
  return {
    canonicalJson: jsonText.value.trim(),
    // gameSystem/ruleset left blank so the backend routes to the source adapter.
    gameSystem: '',
    ruleset: '',
    campaignId: props.campaignId ?? null,
    originalFileName: originalFileName.value,
  }
}

async function submitFile(): Promise<void> {
  localErrors.value = []
  const trimmed = jsonText.value.trim()
  if (!trimmed) {
    localErrors.value = ['JSON content is required. Select a .json file.']
    return
  }
  try {
    JSON.parse(trimmed)
  } catch {
    localErrors.value = ['The file is not valid JSON. Please check the content and try again.']
    return
  }

  submitting.value = true
  try {
    const options: { source?: string; gameSystemDefinitionId?: string } = {}
    if (selectedSource.value) options.source = selectedSource.value
    if (selectedGameSystemId.value) options.gameSystemDefinitionId = selectedGameSystemId.value

    const response = await store.importCharacterFromSource(buildBaseRequest(), options)
    importResponse.value = response

    if (response.requiresSourceConfirmation) {
      // Pre-select the detected source so the dropdown reflects the guess.
      selectedSource.value = response.detectedSource === 'Unknown' ? '' : response.detectedSource
    }
    if (response.review.requiresGameSystemSelection) {
      await ensureGameSystems()
    }
  } catch (err: unknown) {
    importResponse.value = null
    localErrors.value = [extractErr(err)]
  } finally {
    submitting.value = false
  }
}

/** Re-POST after the user picks a source and (optionally) a game system. */
async function reImportWithSelection(): Promise<void> {
  await submitFile()
}

// ---------------------------------------------------------------------------
// D&D Beyond URL submit
// ---------------------------------------------------------------------------

async function submitDndBeyondUrl(): Promise<void> {
  localErrors.value = []
  const url = dndbeyondUrl.value.trim()
  if (!url) {
    localErrors.value = ['Enter a D&D Beyond character URL or ID.']
    return
  }

  submitting.value = true
  try {
    const response = await store.importCharacterFromDndBeyondUrl({
      characterUrl: url,
      campaignId: props.campaignId ?? null,
    })
    importResponse.value = response
  } catch (err: unknown) {
    importResponse.value = null
    localErrors.value = [extractErr(err)]
  } finally {
    submitting.value = false
  }
}

function extractErr(err: unknown): string {
  if (
    err &&
    typeof err === 'object' &&
    'response' in err &&
    err.response &&
    typeof err.response === 'object' &&
    'data' in err.response
  ) {
    const data = (err.response as { data?: { detail?: string; title?: string } }).data
    if (data?.detail) return data.detail
    if (data?.title) return data.title
  }
  return err instanceof Error ? err.message : 'Import failed.'
}

// ---------------------------------------------------------------------------
// Review panel results
// ---------------------------------------------------------------------------

function handleConfirmed(): void {
  emit('completed')
  reset()
}

function handleReviewCancel(): void {
  importResponse.value = null
}

// ---------------------------------------------------------------------------
// Reset / close
// ---------------------------------------------------------------------------

function reset(): void {
  jsonText.value = ''
  originalFileName.value = null
  dndbeyondUrl.value = ''
  selectedSource.value = ''
  selectedGameSystemId.value = ''
  importResponse.value = null
  localErrors.value = []
  activeTab.value = 'file'
  submitting.value = false
}

function handleClose(): void {
  reset()
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4"
      aria-hidden="true"
      @click="handleClose"
    >
      <dialog
        open
        class="m-0 flex max-h-[90vh] w-full max-w-2xl flex-col rounded-xl border border-gray-700 bg-gray-900 p-0 shadow-2xl"
        aria-labelledby="import-modal-title"
        @click.stop
        @keydown.esc="handleClose"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
          <h2 id="import-modal-title" class="text-lg font-semibold text-white">Import Character</h2>
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

        <!-- Tabs -->
        <div
          v-if="!showReview"
          class="flex gap-1 border-b border-gray-800 px-6 pt-3"
          role="tablist"
          aria-label="Import source"
        >
          <button
            v-for="tab in tabs"
            :key="tab.id"
            type="button"
            role="tab"
            :aria-selected="activeTab === tab.id"
            :class="[
              'rounded-t-lg px-4 py-2 text-sm font-medium focus:outline-none focus:ring-2 focus:ring-aircane-400',
              activeTab === tab.id
                ? 'border-b-2 border-aircane-500 text-white'
                : 'text-gray-400 hover:text-gray-200',
            ]"
            @click="switchTab(tab.id)"
          >
            {{ tab.label }}
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 space-y-5 overflow-y-auto px-6 py-5">
          <!-- Review panel (shown after a successful import) -->
          <ImportReviewPanel
            v-if="showReview && importResponse"
            :response="importResponse"
            @confirmed="handleConfirmed"
            @cancel="handleReviewCancel"
          />

          <template v-else>
            <!-- ── Upload File tab ───────────────────────────────────────── -->
            <div v-if="activeTab === 'file'" class="space-y-4">
              <p class="text-sm text-gray-300">
                Select a character
                <code class="rounded bg-gray-800 px-1 py-0.5 text-xs text-aircane-300">.json</code>
                export from a supported virtual tabletop or character builder.
              </p>
              <input
                ref="fileInput"
                type="file"
                accept=".json,application/json"
                class="sr-only"
                aria-label="Select JSON file"
                @change="handleFileChange"
              />
              <div class="flex items-center gap-3">
                <button
                  type="button"
                  class="inline-flex items-center gap-2 rounded-lg border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
                  @click="triggerFileInput"
                >
                  Choose File
                </button>
                <span v-if="originalFileName" class="text-sm text-gray-400">{{ originalFileName }}</span>
              </div>

              <!-- Detected-source badge -->
              <div v-if="detectedBadge" class="flex items-center gap-2 text-sm text-gray-300">
                <span>Detected:</span>
                <span
                  data-testid="detected-source-badge"
                  :class="[
                    'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold',
                    detectedBadge.classes,
                  ]"
                >
                  {{ detectedBadge.label }}
                </span>
              </div>

              <!-- Source confirmation dropdown -->
              <div v-if="needsSourceConfirmation">
                <label for="source-select" class="mb-1 block text-xs font-medium text-gray-300">
                  We couldn't confidently detect the format. Pick the source:
                </label>
                <select
                  id="source-select"
                  v-model="selectedSource"
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                >
                  <option v-for="opt in SOURCE_OPTIONS" :key="opt.value" :value="opt.value">
                    {{ opt.label }}
                  </option>
                </select>
              </div>

              <!-- Game-system picker (Roll20 / Generic) -->
              <div v-if="needsGameSystemSelection">
                <label for="game-system-select" class="mb-1 block text-xs font-medium text-gray-300">
                  This sheet doesn't name a game system. Choose one:
                </label>
                <select
                  id="game-system-select"
                  v-model="selectedGameSystemId"
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                >
                  <option value="">Select a game system…</option>
                  <option v-for="sys in gameSystems" :key="sys.id" :value="sys.id">
                    {{ sys.name }}
                  </option>
                </select>
              </div>

              <!-- Validation errors -->
              <div
                v-if="localErrors.length > 0"
                role="alert"
                class="rounded-lg border border-red-800 bg-red-950 px-4 py-3"
              >
                <ul class="list-inside list-disc space-y-0.5 text-sm text-red-300">
                  <li v-for="(err, i) in localErrors" :key="i">{{ err }}</li>
                </ul>
              </div>

              <div class="flex items-center justify-end gap-3 border-t border-gray-800 pt-4">
                <button
                  type="button"
                  class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
                  @click="handleClose"
                >
                  Cancel
                </button>
                <button
                  v-if="needsSourceConfirmation || needsGameSystemSelection"
                  type="button"
                  :disabled="submitting"
                  class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
                  @click="reImportWithSelection"
                >
                  {{ submitting ? 'Importing…' : 'Continue' }}
                </button>
                <button
                  v-else
                  type="button"
                  :disabled="submitting || !hasJsonContent"
                  class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
                  @click="submitFile"
                >
                  {{ submitting ? 'Importing…' : 'Import Character' }}
                </button>
              </div>
            </div>

            <!-- ── D&D Beyond URL tab ────────────────────────────────────── -->
            <div v-else-if="activeTab === 'url'" class="space-y-4">
              <div>
                <label for="ddb-url" class="mb-1 block text-sm font-medium text-gray-300">
                  D&D Beyond character URL
                </label>
                <input
                  id="ddb-url"
                  v-model="dndbeyondUrl"
                  type="url"
                  placeholder="https://www.dndbeyond.com/characters/..."
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                />
                <p class="mt-1 text-xs text-gray-400">
                  Make sure your character is public under
                  <!-- <a
                    href="https://dndbeyond-support.wizards.com/hc/en-us/articles/7747238449556-Export-Sheet"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="text-aircane-300 underline hover:text-aircane-200"
                  > -->
                    sharing settings
                  <!-- </a>. -->
                </p>
              </div>

              <p class="rounded-lg border border-amber-800 bg-amber-950/40 px-3 py-2 text-xs text-amber-300">
                Uses an unofficial API — may not always work.
              </p>

              <div
                v-if="localErrors.length > 0"
                role="alert"
                class="rounded-lg border border-red-800 bg-red-950 px-4 py-3"
              >
                <ul class="list-inside list-disc space-y-0.5 text-sm text-red-300">
                  <li v-for="(err, i) in localErrors" :key="i">{{ err }}</li>
                </ul>
              </div>

              <div class="flex items-center justify-end gap-3 border-t border-gray-800 pt-4">
                <button
                  type="button"
                  class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
                  @click="handleClose"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  :disabled="submitting || dndbeyondUrl.trim().length === 0"
                  class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
                  @click="submitDndBeyondUrl"
                >
                  {{ submitting ? 'Importing…' : 'Import' }}
                </button>
              </div>
            </div>
          </template>
        </div>
      </dialog>
    </div>
  </Teleport>
</template>

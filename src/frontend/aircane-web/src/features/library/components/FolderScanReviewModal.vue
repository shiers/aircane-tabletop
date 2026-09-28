<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useLibraryStore } from '../store'
import {
  SourceType,
  ScanCandidateFlag,
  type FolderScanPreviewDto,
  type ScanCandidateDto,
  type FolderImportSelectionItem,
} from '../api'

const props = defineProps<{
  /** Controls modal visibility. */
  open: boolean
  /** The folder being reviewed. */
  folderId: string | null
  /** Display name of the folder, for the header. */
  folderName: string
  /** The preview to review. Null while loading. */
  preview: FolderScanPreviewDto | null
}>()

const emit = defineEmits<{ close: []; imported: [] }>()

const store = useLibraryStore()

/** Per-file editable selection state, keyed by sourcePath. */
interface RowState {
  import: boolean
  title: string
  sourceType: SourceType
  ruleset: string
}

const rows = ref<Record<string, RowState>>({})
const importing = ref(false)
const localError = ref<string | null>(null)

const sourceTypeOptions: { label: string; value: SourceType }[] = [
  { label: 'Rules', value: SourceType.Rules },
  { label: 'Adventure', value: SourceType.Adventure },
  { label: 'Solo Module', value: SourceType.Solo },
  { label: 'Character', value: SourceType.Character },
  { label: 'Homebrew', value: SourceType.Homebrew },
  { label: 'Unknown', value: SourceType.Unknown },
]

// Ruleset suggestions from already-imported documents (guides toward consistent values).
const rulesetSuggestions = computed<string[]>(() => {
  const seen = new Set<string>()
  for (const doc of store.documents) {
    if (doc.ruleset) seen.add(doc.ruleset)
  }
  return [...seen].sort((a, b) => a.localeCompare(b))
})

/**
 * Initialize row state whenever a new preview arrives. Nothing is pre-selected — the host always
 * picks — which is especially important for duplicate variants where auto-choosing a winner would
 * be a guess.
 */
watch(
  () => props.preview,
  (preview) => {
    const next: Record<string, RowState> = {}
    if (preview) {
      for (const c of preview.candidates) {
        next[c.sourcePath] = {
          import: false,
          title: c.suggestedTitle,
          sourceType: SourceType.Rules,
          ruleset: c.suggestedRuleset ?? '',
        }
      }
    }
    rows.value = next
    localError.value = null
  },
  { immediate: true },
)

/** Candidates grouped by dedup key, preserving first-seen order. Single-item groups render flat. */
const groups = computed<{ key: string; candidates: ScanCandidateDto[] }[]>(() => {
  const preview = props.preview
  if (!preview) return []
  const byKey = new Map<string, ScanCandidateDto[]>()
  const order: string[] = []
  for (const c of preview.candidates) {
    const key = c.dedupKey || c.sourcePath
    if (!byKey.has(key)) {
      byKey.set(key, [])
      order.push(key)
    }
    byKey.get(key)!.push(c)
  }
  return order.map((key) => ({ key, candidates: byKey.get(key)! }))
})

const selectedCount = computed(() => Object.values(rows.value).filter((r) => r.import).length)
const totalCount = computed(() => props.preview?.candidates.length ?? 0)

function flagLabel(flag: ScanCandidateFlag): string {
  switch (flag) {
    case ScanCandidateFlag.DuplicateVariant:
      return 'Possible duplicate'
    case ScanCandidateFlag.AlreadyImported:
      return 'Already in library'
    case ScanCandidateFlag.LikelyNotRules:
      return 'Maybe not rules'
    default:
      return 'Flagged'
  }
}

function flagClasses(flag: ScanCandidateFlag): string {
  switch (flag) {
    case ScanCandidateFlag.DuplicateVariant:
      return 'bg-amber-900 text-amber-300 ring-1 ring-inset ring-amber-700/50'
    case ScanCandidateFlag.AlreadyImported:
      return 'bg-blue-900 text-blue-300 ring-1 ring-inset ring-blue-700/50'
    case ScanCandidateFlag.LikelyNotRules:
      return 'bg-orange-900 text-orange-300 ring-1 ring-inset ring-orange-700/50'
    default:
      return 'bg-gray-800 text-gray-300 ring-1 ring-inset ring-gray-600/50'
  }
}

/** Select every candidate for import (convenience for clean folders). */
function selectAll(): void {
  for (const key of Object.keys(rows.value)) {
    rows.value[key].import = true
  }
}

/** Deselect everything. */
function selectNone(): void {
  for (const key of Object.keys(rows.value)) {
    rows.value[key].import = false
  }
}

async function confirmImport(): Promise<void> {
  if (!props.folderId) return
  localError.value = null

  const items: FolderImportSelectionItem[] = Object.entries(rows.value).map(([sourcePath, r]) => ({
    sourcePath,
    import: r.import,
    title: r.title.trim() || undefined,
    sourceType: r.sourceType,
    ruleset: r.ruleset.trim() || undefined,
  }))

  importing.value = true
  try {
    await store.importFolderSelection(props.folderId, items)
    emit('imported')
    emit('close')
  } catch {
    localError.value = store.foldersError ?? 'Import failed.'
  } finally {
    importing.value = false
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="scan-review-title"
      @click.self="emit('close')"
    >
      <div
        class="flex max-h-[88vh] w-full max-w-4xl flex-col overflow-hidden rounded-xl border border-gray-700 bg-gray-900 shadow-2xl"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b border-gray-700 px-5 py-4">
          <div>
            <h2 id="scan-review-title" class="text-lg font-semibold text-white">
              Review files to import
            </h2>
            <p class="mt-0.5 text-xs text-gray-400">{{ folderName }}</p>
          </div>
          <button
            class="rounded p-1 text-gray-400 hover:bg-gray-800 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            aria-label="Close"
            @click="emit('close')"
          >
            <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Toolbar -->
        <div class="flex items-center justify-between gap-3 border-b border-gray-800 px-5 py-2.5">
          <p class="text-sm text-gray-400">
            <span class="font-medium text-gray-200">{{ selectedCount }}</span> of
            {{ totalCount }} selected
          </p>
          <div class="flex gap-2">
            <button
              type="button"
              class="rounded px-2.5 py-1 text-xs font-medium text-aircane-400 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              @click="selectAll"
            >
              Select all
            </button>
            <button
              type="button"
              class="rounded px-2.5 py-1 text-xs font-medium text-gray-400 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-gray-500"
              @click="selectNone"
            >
              Clear
            </button>
          </div>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-5 py-4">
          <!-- Loading -->
          <div v-if="!preview" class="py-10 text-center text-sm text-gray-500" aria-busy="true">
            Analyzing folder…
          </div>

          <!-- Empty -->
          <div
            v-else-if="preview.candidates.length === 0"
            class="py-10 text-center text-sm text-gray-500"
          >
            No importable files found in this folder.
          </div>

          <!-- Candidate groups -->
          <div v-else class="space-y-4">
            <div
              v-for="group in groups"
              :key="group.key"
              :class="[
                'rounded-lg',
                group.candidates.length > 1
                  ? 'border border-amber-800/50 bg-amber-950/20 p-3'
                  : '',
              ]"
            >
              <p
                v-if="group.candidates.length > 1"
                class="mb-2 text-xs font-medium text-amber-300"
              >
                {{ group.candidates.length }} possible copies of the same title — pick the one to keep.
              </p>

              <div class="space-y-3">
                <div
                  v-for="candidate in group.candidates"
                  :key="candidate.sourcePath"
                  class="rounded-lg border border-gray-800 bg-gray-900 p-3"
                >
                  <div class="flex items-start gap-3">
                    <!-- Import checkbox -->
                    <input
                      :id="`import-${candidate.sourcePath}`"
                      v-model="rows[candidate.sourcePath].import"
                      type="checkbox"
                      class="mt-1 h-4 w-4 shrink-0 rounded border-gray-600 bg-gray-800 text-aircane-500 focus:ring-2 focus:ring-aircane-500"
                    />

                    <div class="min-w-0 flex-1">
                      <!-- Filename + flags -->
                      <div class="flex flex-wrap items-center gap-2">
                        <span class="truncate font-mono text-xs text-gray-400" :title="candidate.fileName">
                          {{ candidate.fileName }}
                        </span>
                        <span
                          v-for="flag in candidate.flags"
                          :key="flag"
                          class="inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-medium"
                          :class="flagClasses(flag)"
                        >
                          {{ flagLabel(flag) }}
                        </span>
                      </div>

                      <!-- Reason -->
                      <p v-if="candidate.reason" class="mt-1 text-xs text-gray-500">
                        {{ candidate.reason }}
                      </p>

                      <!-- Editable fields (shown when selected for import) -->
                      <div
                        v-if="rows[candidate.sourcePath].import"
                        class="mt-3 grid gap-3 sm:grid-cols-3"
                      >
                        <div class="sm:col-span-3">
                          <label
                            :for="`title-${candidate.sourcePath}`"
                            class="mb-1 block text-xs font-medium text-gray-400"
                          >
                            Title
                          </label>
                          <input
                            :id="`title-${candidate.sourcePath}`"
                            v-model="rows[candidate.sourcePath].title"
                            type="text"
                            class="block w-full rounded-md border border-gray-700 bg-gray-800 px-2.5 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                          />
                        </div>
                        <div>
                          <label
                            :for="`type-${candidate.sourcePath}`"
                            class="mb-1 block text-xs font-medium text-gray-400"
                          >
                            Type
                          </label>
                          <select
                            :id="`type-${candidate.sourcePath}`"
                            v-model.number="rows[candidate.sourcePath].sourceType"
                            class="block w-full rounded-md border border-gray-700 bg-gray-800 px-2.5 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                          >
                            <option v-for="opt in sourceTypeOptions" :key="opt.value" :value="opt.value">
                              {{ opt.label }}
                            </option>
                          </select>
                        </div>
                        <div class="sm:col-span-2">
                          <label
                            :for="`ruleset-${candidate.sourcePath}`"
                            class="mb-1 block text-xs font-medium text-gray-400"
                          >
                            Ruleset
                          </label>
                          <input
                            :id="`ruleset-${candidate.sourcePath}`"
                            v-model="rows[candidate.sourcePath].ruleset"
                            type="text"
                            list="scan-ruleset-options"
                            autocomplete="off"
                            placeholder="e.g. 2014"
                            class="block w-full rounded-md border border-gray-700 bg-gray-800 px-2.5 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                          />
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <datalist id="scan-ruleset-options">
              <option v-for="r in rulesetSuggestions" :key="r" :value="r" />
            </datalist>
          </div>
        </div>

        <!-- Footer -->
        <div class="border-t border-gray-700 px-5 py-4">
          <p v-if="localError" role="alert" class="mb-2 text-sm text-red-400">{{ localError }}</p>
          <div class="flex items-center justify-end gap-3">
            <button
              type="button"
              class="rounded-lg px-4 py-2 text-sm font-medium text-gray-300 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-gray-500"
              @click="emit('close')"
            >
              Cancel
            </button>
            <button
              type="button"
              :disabled="importing || selectedCount === 0"
              class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
              @click="confirmImport"
            >
              <svg
                v-if="importing"
                class="h-4 w-4 animate-spin"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              {{ importing ? 'Importing…' : `Import ${selectedCount} file${selectedCount === 1 ? '' : 's'}` }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </Teleport>
</template>

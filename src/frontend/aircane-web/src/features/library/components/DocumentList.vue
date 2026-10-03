<script setup lang="ts">
import { computed, ref } from 'vue'
import { useLibraryStore } from '../store'
import { ImportStatus, SourceType, type SourceDocumentDto } from '../api'
import DocumentStatusBadge from './DocumentStatusBadge.vue'
import AircaneImg from '@/shared/components/AircaneImg.vue'
import ThumbnailPlaceholder from '@/shared/components/ThumbnailPlaceholder.vue'

const store = useLibraryStore()

const deletingId = ref<string | null>(null)
const reindexingId = ref<string | null>(null)
const reOcrId = ref<string | null>(null)
const togglingId = ref<string | null>(null)

/** Currently selected document shown in the detail side panel. */
const selectedId = ref<string | null>(null)

const selectedDoc = computed<SourceDocumentDto | null>(
  () => store.documents.find((d) => d.id === selectedId.value) ?? null,
)

function selectDocument(doc: SourceDocumentDto): void {
  selectedId.value = selectedId.value === doc.id ? null : doc.id
}

/** Map the backend ImportStatus enum onto DocumentStatusBadge's string states. */
function badgeStatus(
  status: ImportStatus,
): 'parsed' | 'processing' | 'error' | 'ocr-required' {
  switch (status) {
    case ImportStatus.Completed:
      return 'parsed'
    case ImportStatus.Failed:
      return 'error'
    case ImportStatus.OcrRequired:
      return 'ocr-required'
    // Pending and Processing both read as in-flight "processing".
    default:
      return 'processing'
  }
}

/**
 * Pick a row thumbnail by detected source type:
 * - Rules → SRD by default, PF2e when the system/ruleset reads as Pathfinder 2e
 * - Adventure → Lost Mine
 * - Homebrew → Homebrew Monsters
 * Other types fall back to the SRD thumbnail.
 */
function thumbnailFor(doc: SourceDocumentDto): string {
  const base = '/assets/library'
  const haystack = `${doc.gameSystem ?? ''} ${doc.ruleset ?? ''}`.toLowerCase()
  const isPf2e = haystack.includes('pathfinder') || haystack.includes('pf2e')
  switch (doc.sourceType) {
    case SourceType.Rules:
      return isPf2e
        ? `${base}/document-thumbnail-pf2e.png`
        : `${base}/document-thumbnail-srd.png`
    case SourceType.Adventure:
    case SourceType.Solo:
      return `${base}/document-thumbnail-lost-mine.png`
    case SourceType.Homebrew:
      return `${base}/document-thumbnail-homebrew-monsters.png`
    default:
      return `${base}/document-thumbnail-srd.png`
  }
}

/** Tailwind classes for a license badge by license key. */
function licenseBadgeClasses(licenseKey: string | null): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300'
    case 'orc':
      return 'bg-purple-900 text-purple-300'
    default:
      return 'bg-gray-800 text-gray-300'
  }
}

const sourceTypeLabel: Record<SourceType, string> = {
  [SourceType.Unknown]: 'Unknown',
  [SourceType.Rules]: 'Rules',
  [SourceType.Adventure]: 'Adventure',
  [SourceType.Solo]: 'Solo',
  [SourceType.Character]: 'Character',
  [SourceType.Homebrew]: 'Homebrew',
  [SourceType.Generated]: 'Generated',
}

/** Documents that have failed import - used to show per-row error details. */
const failedDocuments = computed(() =>
  store.documents.filter((d) => d.importStatus === ImportStatus.Failed),
)

/** Build a map of folderId → folder display name for quick lookup. */
const folderNameById = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {}
  for (const f of store.folders) {
    map[f.id] = f.displayName
  }
  return map
})

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function errorMessageFor(doc: SourceDocumentDto): string | null {
  return store.statusMap[doc.id]?.errorMessage ?? null
}

async function handleDelete(doc: SourceDocumentDto): Promise<void> {
  if (!confirm(`Delete "${doc.title}"? This cannot be undone.`)) return
  deletingId.value = doc.id
  try {
    await store.deleteDocument(doc.id)
  } finally {
    deletingId.value = null
  }
}

async function handleReOcr(doc: SourceDocumentDto): Promise<void> {
  reOcrId.value = doc.id
  try {
    await store.reOcrDocument(doc.id)
  } finally {
    reOcrId.value = null
  }
}

async function handleReindex(doc: SourceDocumentDto): Promise<void> {
  reindexingId.value = doc.id
  try {
    await store.reindexDocument(doc.id)
  } finally {
    reindexingId.value = null
  }
}

async function handleToggleDisabled(doc: SourceDocumentDto): Promise<void> {
  togglingId.value = doc.id
  try {
    await store.setDocumentDisabled(doc.id, !doc.isDisabled)
  } finally {
    togglingId.value = null
  }
}
</script>

<template>
  <section aria-labelledby="library-heading">
    <h2 id="library-heading" class="mb-4 text-lg font-semibold text-white">
      Document Library
    </h2>

    <!-- Loading skeleton -->
    <div v-if="store.loading && store.documents.length === 0" class="space-y-2" aria-busy="true" aria-label="Loading documents">
      <div v-for="n in 3" :key="n" class="h-12 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!store.loading && store.documents.length === 0"
      class="flex flex-col items-center py-16 text-center text-gray-500"
    >
      <img
        src="/assets/library/library-empty-state-ornament.png"
        alt=""
        aria-hidden="true"
        class="mb-4 w-full max-w-[280px]"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />
      <p class="text-sm">No documents yet. Upload one above or register a folder to get started.</p>
    </div>

    <!-- Document table + detail side panel -->
    <div v-else class="flex flex-col gap-4 lg:flex-row">
    <div class="min-w-0 flex-1 overflow-x-auto rounded-xl border border-gray-800">
      <table class="w-full text-left text-sm text-gray-300" aria-label="Imported documents">
        <thead class="border-b border-gray-800 bg-gray-900 text-xs uppercase tracking-wider text-gray-500">
          <tr>
            <th scope="col" class="px-4 py-3"><span class="sr-only">Thumbnail</span></th>
            <th scope="col" class="px-4 py-3">Title</th>
            <th scope="col" class="px-4 py-3">Type</th>
            <th scope="col" class="px-4 py-3">System / Ruleset</th>
            <th scope="col" class="px-4 py-3">Folder</th>
            <th scope="col" class="px-4 py-3">Status</th>
            <th scope="col" class="px-4 py-3">Added</th>
            <th scope="col" class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-800 bg-gray-950">
          <template v-for="doc in store.documents" :key="doc.id">
            <tr
              class="document-row cursor-pointer hover:bg-gray-900"
              :class="[
                selectedId === doc.id ? 'selected' : '',
                !doc.isSourceAvailable ? 'bg-orange-950/20' : '',
              ]"
              :aria-selected="selectedId === doc.id"
              @click="selectDocument(doc)"
            >
              <!-- Source-type thumbnail -->
              <td class="px-4 py-3">
                <AircaneImg
                  :src="thumbnailFor(doc)"
                  alt=""
                  type="thumbnail"
                  aria-hidden="true"
                  class="h-10 w-10 rounded"
                >
                  <template #fallback>
                    <ThumbnailPlaceholder :label="doc.title" />
                  </template>
                </AircaneImg>
              </td>

              <!-- Title + filename + source-unavailable badge -->
              <td class="px-4 py-3">
                <div class="flex flex-wrap items-center gap-2">
                  <p class="font-medium text-white">{{ doc.title }}</p>
                  <!-- Built-in badge -->
                  <span
                    v-if="doc.isBuiltIn"
                    class="inline-flex items-center rounded-full bg-aircane-900 px-2 py-0.5 text-xs font-medium text-aircane-300"
                    title="Built-in content shipped with Aircane. Cannot be deleted; disable to exclude from retrieval."
                  >
                    Built-in
                  </span>
                  <!-- License badge -->
                  <span
                    v-if="doc.licenseKey"
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                    :class="licenseBadgeClasses(doc.licenseKey)"
                    :title="doc.licenseDisplayName ?? doc.licenseKey"
                  >
                    {{ doc.licenseDisplayName ?? doc.licenseKey }}
                  </span>
                  <!-- Disabled badge -->
                  <span
                    v-if="doc.isDisabled"
                    class="inline-flex items-center rounded-full bg-gray-700 px-2 py-0.5 text-xs font-medium text-gray-300"
                    title="This document is disabled and excluded from retrieval."
                  >
                    Disabled
                  </span>
                  <!-- Source unavailable warning badge -->
                  <span
                    v-if="!doc.isSourceAvailable"
                    class="inline-flex items-center gap-1 rounded-full bg-orange-900 px-2 py-0.5 text-xs font-medium text-orange-300"
                    title="The source file for this document is no longer accessible. Re-scan the folder or re-upload the file."
                    aria-label="Source file unavailable"
                  >
                    <svg
                      class="h-3 w-3"
                      xmlns="http://www.w3.org/2000/svg"
                      viewBox="0 0 20 20"
                      fill="currentColor"
                      aria-hidden="true"
                    >
                      <path
                        fill-rule="evenodd"
                        d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495zM10 5a.75.75 0 01.75.75v3.5a.75.75 0 01-1.5 0v-3.5A.75.75 0 0110 5zm0 9a1 1 0 100-2 1 1 0 000 2z"
                        clip-rule="evenodd"
                      />
                    </svg>
                    Source unavailable
                  </span>
                </div>
                <p class="text-xs text-gray-500">{{ doc.originalFileName }}</p>
              </td>

              <!-- Source type -->
              <td class="px-4 py-3">{{ sourceTypeLabel[doc.sourceType] }}</td>

              <!-- Game system / ruleset -->
              <td class="px-4 py-3">
                <span v-if="doc.gameSystem || doc.ruleset">
                  {{ [doc.gameSystem, doc.ruleset].filter(Boolean).join(' · ') }}
                </span>
                <span v-else class="text-gray-600">-</span>
              </td>

              <!-- Folder name -->
              <td class="px-4 py-3">
                <span v-if="doc.watchedFolderId && folderNameById[doc.watchedFolderId]" class="text-gray-400">
                  {{ folderNameById[doc.watchedFolderId] }}
                </span>
                <span v-else class="text-gray-600">-</span>
              </td>

              <!-- Status badge -->
              <td class="px-4 py-3">
                <DocumentStatusBadge :status="badgeStatus(doc.importStatus)" />
              </td>

              <!-- Date -->
              <td class="px-4 py-3 text-gray-400">{{ formatDate(doc.createdAt) }}</td>

              <!-- Actions -->
              <td class="px-4 py-3 text-right" @click.stop>
                <div class="flex items-center justify-end gap-2">
                  <!-- Reindex button - shown for failed or OCR-required docs -->
                  <button
                    v-if="doc.importStatus === ImportStatus.Failed || doc.importStatus === ImportStatus.OcrRequired"
                    :disabled="reindexingId === doc.id"
                    :aria-label="`Re-run import for ${doc.title}`"
                    class="rounded px-2 py-1 text-xs font-medium text-blue-400 hover:bg-gray-800 hover:text-blue-300 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
                    @click="handleReindex(doc)"
                  >
                    <span v-if="reindexingId === doc.id">Re-indexing…</span>
                    <span v-else>Re-index</span>
                  </button>

                  <!-- Re-run OCR button - shown for OCR-required docs (use after enabling OCR) -->
                  <button
                    v-if="doc.importStatus === ImportStatus.OcrRequired"
                    :disabled="reOcrId === doc.id"
                    :aria-label="`Re-run OCR for ${doc.title}`"
                    class="rounded px-2 py-1 text-xs font-medium text-yellow-400 hover:bg-gray-800 hover:text-yellow-300 focus:outline-none focus:ring-2 focus:ring-yellow-500 disabled:opacity-50"
                    @click="handleReOcr(doc)"
                  >
                    <span v-if="reOcrId === doc.id">Re-running OCR…</span>
                    <span v-else>Re-run OCR</span>
                  </button>

                  <!-- Built-in: Disable/Enable toggle (built-in content cannot be deleted) -->
                  <button
                    v-if="doc.isBuiltIn"
                    :disabled="togglingId === doc.id"
                    :aria-label="`${doc.isDisabled ? 'Enable' : 'Disable'} ${doc.title}`"
                    :title="doc.isDisabled
                      ? 'Enable this built-in content so it is included in retrieval.'
                      : 'Built-in content cannot be deleted. Disable to exclude from retrieval.'"
                    class="rounded px-2 py-1 text-xs font-medium text-amber-400 hover:bg-gray-800 hover:text-amber-300 focus:outline-none focus:ring-2 focus:ring-amber-500 disabled:opacity-50"
                    @click="handleToggleDisabled(doc)"
                  >
                    <span v-if="togglingId === doc.id">Updating…</span>
                    <span v-else>{{ doc.isDisabled ? 'Enable' : 'Disable' }}</span>
                  </button>

                  <!-- Non-built-in: Delete button -->
                  <button
                    v-else
                    :disabled="deletingId === doc.id"
                    :aria-label="`Delete ${doc.title}`"
                    class="rounded px-2 py-1 text-xs font-medium text-red-400 hover:bg-gray-800 hover:text-red-300 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                    @click="handleDelete(doc)"
                  >
                    <span v-if="deletingId === doc.id">Deleting…</span>
                    <span v-else>Delete</span>
                  </button>
                </div>
              </td>
            </tr>

            <!-- Inline error row for failed documents -->
            <tr
              v-if="doc.importStatus === ImportStatus.Failed && errorMessageFor(doc)"
              :key="`${doc.id}-error`"
              class="bg-red-950"
            >
              <td colspan="8" class="px-4 py-2 text-xs text-red-300">
                <span class="font-semibold">Import error:</span>
                {{ errorMessageFor(doc) }}
              </td>
            </tr>

            <!-- Inline source-unavailable detail row -->
            <tr
              v-if="!doc.isSourceAvailable"
              :key="`${doc.id}-unavailable`"
              class="bg-orange-950/30"
            >
              <td colspan="8" class="px-4 py-2 text-xs text-orange-300">
                <span class="font-semibold">Source unavailable:</span>
                The file at the registered path can no longer be accessed. The document's indexed
                content is still available for search, but re-indexing will fail until the file is
                restored. Re-scan the folder once the file is accessible again.
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

      <!-- Document detail side panel (metadata/actions/chunk info in the upper 60%). -->
      <aside
        v-if="selectedDoc"
        class="document-detail-panel"
        aria-label="Document details"
        v-bg-asset="{ url: '/assets/library/document-detail-side-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <div class="document-detail-content">
          <div class="flex items-start justify-between gap-2">
            <h3 class="text-base font-semibold text-white">{{ selectedDoc.title }}</h3>
            <button
              type="button"
              class="rounded px-2 py-0.5 text-xs text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
              aria-label="Close details"
              @click="selectedId = null"
            >
              ✕
            </button>
          </div>
          <p class="mt-0.5 truncate text-xs text-gray-400" :title="selectedDoc.originalFileName">
            {{ selectedDoc.originalFileName }}
          </p>

          <dl class="mt-3 space-y-1.5 text-xs">
            <div class="flex justify-between gap-3">
              <dt class="text-gray-500">Type</dt>
              <dd class="text-gray-200">{{ sourceTypeLabel[selectedDoc.sourceType] }}</dd>
            </div>
            <div class="flex justify-between gap-3">
              <dt class="text-gray-500">System / Ruleset</dt>
              <dd class="text-gray-200">
                {{ [selectedDoc.gameSystem, selectedDoc.ruleset].filter(Boolean).join(' · ') || '-' }}
              </dd>
            </div>
            <div class="flex items-center justify-between gap-3">
              <dt class="text-gray-500">Status</dt>
              <dd><DocumentStatusBadge :status="badgeStatus(selectedDoc.importStatus)" /></dd>
            </div>
            <div class="flex justify-between gap-3">
              <dt class="text-gray-500">Added</dt>
              <dd class="text-gray-200">{{ formatDate(selectedDoc.createdAt) }}</dd>
            </div>
          </dl>
        </div>
      </aside>
    </div>
  </section>
</template>

<style scoped>
/* Selected-row highlight — CSS only (no PNG), per the spec. */
.document-row.selected {
  box-shadow: inset 0 0 0 1px var(--color-purple), var(--glow-purple);
  background: rgba(124, 58, 237, 0.08);
}

/* Document detail side panel: art background with content in the upper 60%. */
.document-detail-panel {
  position: relative;
  flex-shrink: 0;
  width: 100%;
  min-height: 320px;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  overflow: hidden;
  background-size: cover;
  background-position: left center;
  background-repeat: no-repeat;
}

@media (min-width: 1024px) {
  .document-detail-panel {
    width: 320px;
  }
}

.document-detail-content {
  position: absolute;
  inset: 0 0 40% 0; /* upper 60% */
  padding: 16px 18px;
  overflow-y: auto;
}
</style>


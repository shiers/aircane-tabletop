<script setup lang="ts">
import { computed, ref } from 'vue'
import { useLibraryStore } from '../store'
import { ImportStatus, SourceType, type SourceDocumentDto } from '../api'
import ImportStatusBadge from './ImportStatusBadge.vue'

const store = useLibraryStore()

const deletingId = ref<string | null>(null)
const reindexingId = ref<string | null>(null)

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

async function handleReindex(doc: SourceDocumentDto): Promise<void> {
  reindexingId.value = doc.id
  try {
    await store.reindexDocument(doc.id)
  } finally {
    reindexingId.value = null
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
      class="rounded-xl border border-dashed border-gray-700 py-16 text-center text-gray-500"
    >
      <p class="text-sm">No documents yet. Upload one above or register a folder to get started.</p>
    </div>

    <!-- Document table -->
    <div v-else class="overflow-x-auto rounded-xl border border-gray-800">
      <table class="w-full text-left text-sm text-gray-300" aria-label="Imported documents">
        <thead class="border-b border-gray-800 bg-gray-900 text-xs uppercase tracking-wider text-gray-500">
          <tr>
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
              :class="[
                'hover:bg-gray-900',
                !doc.isSourceAvailable ? 'bg-orange-950/20' : '',
              ]"
            >
              <!-- Title + filename + source-unavailable badge -->
              <td class="px-4 py-3">
                <div class="flex flex-wrap items-center gap-2">
                  <p class="font-medium text-white">{{ doc.title }}</p>
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
                <ImportStatusBadge :status="doc.importStatus" />
              </td>

              <!-- Date -->
              <td class="px-4 py-3 text-gray-400">{{ formatDate(doc.createdAt) }}</td>

              <!-- Actions -->
              <td class="px-4 py-3 text-right">
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

                  <!-- Delete button -->
                  <button
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
              <td colspan="7" class="px-4 py-2 text-xs text-red-300">
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
              <td colspan="7" class="px-4 py-2 text-xs text-orange-300">
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
  </section>
</template>

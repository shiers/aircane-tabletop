<script setup lang="ts">
import { computed, ref } from 'vue'
import { useLibraryStore } from '../store'
import { SourceType, type WatchedFolderDto } from '../api'
import RegisterFolderForm from './RegisterFolderForm.vue'

const store = useLibraryStore()

const deletingId = ref<string | null>(null)

const sourceTypeLabel: Record<SourceType, string> = {
  [SourceType.Unknown]: 'Unknown',
  [SourceType.Rules]: 'Rules',
  [SourceType.Adventure]: 'Adventure',
  [SourceType.Solo]: 'Solo',
  [SourceType.Character]: 'Character',
  [SourceType.Homebrew]: 'Homebrew',
  [SourceType.Generated]: 'Generated',
}

/** Count of documents per folder that have source unavailable. */
const unavailableCountByFolder = computed<Record<string, number>>(() => {
  const counts: Record<string, number> = {}
  for (const doc of store.documents) {
    if (doc.watchedFolderId && !doc.isSourceAvailable) {
      counts[doc.watchedFolderId] = (counts[doc.watchedFolderId] ?? 0) + 1
    }
  }
  return counts
})

/** Count of documents per folder. */
const docCountByFolder = computed<Record<string, number>>(() => {
  const counts: Record<string, number> = {}
  for (const doc of store.documents) {
    if (doc.watchedFolderId) {
      counts[doc.watchedFolderId] = (counts[doc.watchedFolderId] ?? 0) + 1
    }
  }
  return counts
})

function formatDate(iso: string | null): string {
  if (!iso) return 'Never'
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function isScanningFolder(id: string): boolean {
  return store.scanningFolderIds.has(id)
}

async function handleScan(folder: WatchedFolderDto): Promise<void> {
  await store.scanFolder(folder.id)
}

async function handleDelete(folder: WatchedFolderDto): Promise<void> {
  if (
    !confirm(
      `Unregister "${folder.displayName}"?\n\nThis removes the folder and all its indexed documents from the library. Your source files will not be deleted.`,
    )
  )
    return
  deletingId.value = folder.id
  try {
    await store.deleteFolder(folder.id)
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <section aria-labelledby="folders-heading">
    <div class="mb-4 flex items-center justify-between">
      <h2 id="folders-heading" class="text-lg font-semibold text-white">Watched Folders</h2>
    </div>

    <!-- Folders error -->
    <div
      v-if="store.foldersError"
      role="alert"
      class="mb-4 flex items-start gap-3 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
    >
      <svg
        class="mt-0.5 h-4 w-4 shrink-0 text-red-400"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zm-.75-9.25a.75.75 0 011.5 0v3.5a.75.75 0 01-1.5 0v-3.5zm.75 6a.75.75 0 100-1.5.75.75 0 000 1.5z"
          clip-rule="evenodd"
        />
      </svg>
      <span>{{ store.foldersError }}</span>
    </div>

    <!-- Loading skeleton (only on initial load when no data is cached) -->
    <div
      v-if="store.foldersLoading && store.folders.length === 0"
      class="mb-4 space-y-2"
      aria-busy="true"
      aria-label="Loading folders"
    >
      <div v-for="n in 2" :key="n" class="h-16 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!store.foldersLoading && store.folders.length === 0"
      class="mb-4 rounded-xl border border-dashed border-gray-700 py-10 text-center text-gray-500"
    >
      <svg
        class="mx-auto mb-3 h-8 w-8 text-gray-600"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          d="M2 6a2 2 0 012-2h5l2 2h5a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z"
        />
      </svg>
      <p class="text-sm">No folders registered yet. Register a folder below to get started.</p>
    </div>

    <!-- Folder cards -->
    <div v-else class="mb-4 space-y-3">
      <article
        v-for="folder in store.folders"
        :key="folder.id"
        class="rounded-xl border border-gray-800 bg-gray-900 p-4"
        :aria-label="`Watched folder: ${folder.displayName}`"
      >
        <div class="flex flex-wrap items-start justify-between gap-3">
          <!-- Folder info -->
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <!-- Folder icon -->
              <svg
                class="h-4 w-4 shrink-0 text-aircane-400"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 20 20"
                fill="currentColor"
                aria-hidden="true"
              >
                <path
                  d="M2 6a2 2 0 012-2h5l2 2h5a2 2 0 012 2v6a2 2 0 01-2 2H4a2 2 0 01-2-2V6z"
                />
              </svg>

              <h3 class="font-semibold text-white">{{ folder.displayName }}</h3>

              <!-- Source type badge -->
              <span
                class="rounded-full bg-gray-700 px-2 py-0.5 text-xs font-medium text-gray-300"
              >
                {{ sourceTypeLabel[folder.defaultSourceType] }}
              </span>

              <!-- Source unavailable warning badge -->
              <span
                v-if="unavailableCountByFolder[folder.id]"
                class="inline-flex items-center gap-1 rounded-full bg-orange-900 px-2 py-0.5 text-xs font-medium text-orange-300"
                :aria-label="`${unavailableCountByFolder[folder.id]} source files unavailable`"
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
                {{ unavailableCountByFolder[folder.id] }} unavailable
              </span>
            </div>

            <!-- Path -->
            <p class="mt-1 truncate font-mono text-xs text-gray-500" :title="folder.absolutePath">
              {{ folder.absolutePath }}
            </p>

            <!-- Metadata row -->
            <div class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-500">
              <span v-if="folder.defaultGameSystem || folder.defaultRuleset">
                {{ [folder.defaultGameSystem, folder.defaultRuleset].filter(Boolean).join(' · ') }}
              </span>
              <span>
                {{ docCountByFolder[folder.id] ?? 0 }}
                {{ (docCountByFolder[folder.id] ?? 0) === 1 ? 'document' : 'documents' }}
              </span>
              <span>Last scanned: {{ formatDate(folder.lastScannedAt) }}</span>
            </div>

            <!-- Scan result summary -->
            <div
              v-if="scanResultMap[folder.id]"
              class="mt-2 text-xs text-gray-500"
              aria-live="polite"
            >
              Last scan: {{ scanResultMap[folder.id].newDocuments }} new,
              {{ scanResultMap[folder.id].updatedDocuments }} updated,
              {{ scanResultMap[folder.id].unchangedDocuments }} unchanged
              <span
                v-if="scanResultMap[folder.id].errors.length > 0"
                class="ml-1 text-orange-400"
              >
                · {{ scanResultMap[folder.id].errors.length }} error(s)
              </span>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex shrink-0 items-center gap-2">
            <button
              :disabled="isScanningFolder(folder.id)"
              :aria-label="`Scan folder ${folder.displayName}`"
              class="inline-flex items-center gap-1.5 rounded px-2.5 py-1.5 text-xs font-medium text-blue-400 hover:bg-gray-800 hover:text-blue-300 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
              @click="handleScan(folder)"
            >
              <svg
                v-if="isScanningFolder(folder.id)"
                class="h-3 w-3 animate-spin"
                xmlns="http://www.w3.org/2000/svg"
                fill="none"
                viewBox="0 0 24 24"
                aria-hidden="true"
              >
                <circle
                  class="opacity-25"
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  stroke-width="4"
                />
                <path
                  class="opacity-75"
                  fill="currentColor"
                  d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                />
              </svg>
              <svg
                v-else
                class="h-3 w-3"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 20 20"
                fill="currentColor"
                aria-hidden="true"
              >
                <path
                  fill-rule="evenodd"
                  d="M15.312 11.424a5.5 5.5 0 01-9.201 2.466l-.312-.311h2.433a.75.75 0 000-1.5H3.989a.75.75 0 00-.75.75v4.242a.75.75 0 001.5 0v-2.43l.31.31a7 7 0 0011.712-3.138.75.75 0 00-1.449-.39zm1.23-3.723a.75.75 0 00.219-.53V2.929a.75.75 0 00-1.5 0V5.36l-.31-.31A7 7 0 003.239 8.188a.75.75 0 101.448.389A5.5 5.5 0 0113.89 6.11l.311.31h-2.432a.75.75 0 000 1.5h4.243a.75.75 0 00.53-.219z"
                  clip-rule="evenodd"
                />
              </svg>
              {{ isScanningFolder(folder.id) ? 'Scanning…' : 'Scan Now' }}
            </button>

            <button
              :disabled="deletingId === folder.id"
              :aria-label="`Unregister folder ${folder.displayName}`"
              class="rounded px-2.5 py-1.5 text-xs font-medium text-red-400 hover:bg-gray-800 hover:text-red-300 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
              @click="handleDelete(folder)"
            >
              {{ deletingId === folder.id ? 'Removing…' : 'Unregister' }}
            </button>
          </div>
        </div>
      </article>
    </div>

    <!-- Register folder form -->
    <RegisterFolderForm />
  </section>
</template>

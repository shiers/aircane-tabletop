<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { useLibraryStore } from './store'
import { ImportStatus } from './api'
import DocumentUploadForm from './components/DocumentUploadForm.vue'
import DocumentList from './components/DocumentList.vue'
import WatchedFolderSection from './components/WatchedFolderSection.vue'

const store = useLibraryStore()

/** True when at least one document needs OCR processing. */
const hasOcrRequired = computed(() =>
  store.documents.some((d) => d.importStatus === ImportStatus.OcrRequired),
)

/** Documents that have failed import. */
const failedDocuments = computed(() =>
  store.documents.filter((d) => d.importStatus === ImportStatus.Failed),
)

/** Documents whose source file is no longer accessible. */
const unavailableDocuments = computed(() =>
  store.documents.filter((d) => !d.isSourceAvailable),
)

onMounted(() => {
  store.fetchDocuments()
  store.fetchFolders()
})

onUnmounted(() => {
  store.stopAllPolling()
})
</script>

<template>
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Page header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-5xl items-center justify-between">
        <div class="flex items-center gap-3">
          <RouterLink
            to="/"
            class="text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            aria-label="Back to home"
          >
            ← Home
          </RouterLink>
          <span class="text-gray-700" aria-hidden="true">/</span>
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Document Library</h1>
        </div>
      </div>
    </header>

    <div class="mx-auto max-w-5xl space-y-6 px-6 py-8">
      <!-- Global store error banner -->
      <div
        v-if="store.error"
        role="alert"
        class="flex items-start gap-3 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
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
        <span>{{ store.error }}</span>
      </div>

      <!-- Source-unavailable warning banner -->
      <div
        v-if="unavailableDocuments.length > 0"
        role="status"
        aria-live="polite"
        class="flex items-start gap-3 rounded-lg border border-orange-700 bg-orange-950 px-4 py-3 text-sm text-orange-300"
      >
        <svg
          class="mt-0.5 h-4 w-4 shrink-0 text-orange-400"
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
        <div>
          <p class="font-semibold text-orange-200">Source Files Unavailable</p>
          <p class="mt-0.5 text-orange-400">
            {{ unavailableDocuments.length }}
            {{ unavailableDocuments.length === 1 ? 'document' : 'documents' }}
            {{ unavailableDocuments.length === 1 ? 'has' : 'have' }} a source file that can no
            longer be accessed. This usually means the file was moved, deleted, or the folder is
            unmounted. Re-scan the folder once the files are accessible again.
          </p>
        </div>
      </div>

      <!-- OCR-required warning banner -->
      <div
        v-if="hasOcrRequired"
        role="status"
        aria-live="polite"
        class="flex items-start gap-3 rounded-lg border border-yellow-700 bg-yellow-950 px-4 py-3 text-sm text-yellow-300"
      >
        <svg
          class="mt-0.5 h-4 w-4 shrink-0 text-yellow-400"
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
        <div>
          <p class="font-semibold text-yellow-200">OCR Required</p>
          <p class="mt-0.5 text-yellow-400">
            One or more documents contain scanned pages that cannot be read as text. OCR processing
            is not yet available in this version. These documents will not be searchable until OCR
            support is added.
          </p>
        </div>
      </div>

      <!-- Failed import error summary -->
      <div
        v-if="failedDocuments.length > 0"
        role="status"
        aria-live="polite"
        class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
      >
        <p class="font-semibold text-red-200">
          {{ failedDocuments.length }}
          {{ failedDocuments.length === 1 ? 'document' : 'documents' }} failed to import.
        </p>
        <ul class="mt-1 list-inside list-disc space-y-0.5 text-red-400">
          <li v-for="doc in failedDocuments" :key="doc.id">
            {{ doc.title }}
            <span v-if="store.statusMap[doc.id]?.errorMessage">
              — {{ store.statusMap[doc.id].errorMessage }}
            </span>
          </li>
        </ul>
        <p class="mt-2 text-red-400">
          Use the <strong class="text-red-300">Re-index</strong> button next to each document to
          retry the import.
        </p>
      </div>

      <!-- Watched folders section -->
      <WatchedFolderSection />

      <!-- Divider -->
      <hr class="border-gray-800" />

      <!-- Upload form -->
      <DocumentUploadForm />

      <!-- Document list -->
      <DocumentList />
    </div>
  </main>
</template>

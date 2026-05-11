<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useCharacterStore } from './store'
import { type CharacterDto, type CreateCharacterRequest, type UpdateCharacterRequest, type ImportCharacterJsonRequest, type CharacterFieldReviewDto } from './api'
import CharacterForm from './components/CharacterForm.vue'
import CharacterList from './components/CharacterList.vue'
import ImportCharacterModal from './components/ImportCharacterModal.vue'
import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** Campaign ID to scope the character list. Required for list/create. */
  campaignId?: string
}>()

// ---------------------------------------------------------------------------
// Store / Router
// ---------------------------------------------------------------------------

const store = useCharacterStore()
const router = useRouter()

// ---------------------------------------------------------------------------
// UI state
// ---------------------------------------------------------------------------

/** null = create mode; CharacterDto = edit mode */
const editingCharacter = ref<CharacterDto | null>(null)
const showForm = ref(false)
const showImportModal = ref(false)
const importModalRef = ref<InstanceType<typeof ImportCharacterModal> | null>(null)

// PDF import state
const pdfFileInput = ref<HTMLInputElement | null>(null)
const pdfImporting = ref(false)
const pdfImportError = ref<string | null>(null)

onMounted(() => {
  if (props.campaignId) {
    store.fetchCharacters(props.campaignId)
  }
})

function openCreateForm(): void {
  editingCharacter.value = null
  showForm.value = true
}

function openEditForm(character: CharacterDto): void {
  editingCharacter.value = character
  showForm.value = true
}

function closeForm(): void {
  showForm.value = false
  editingCharacter.value = null
}

async function handleFormSubmit(payload: CreateCharacterRequest | UpdateCharacterRequest): Promise<void> {
  if (editingCharacter.value) {
    await store.updateCharacter(editingCharacter.value.id, payload as UpdateCharacterRequest)
  } else {
    await store.createCharacter(payload as CreateCharacterRequest)
  }
  closeForm()
}

// ---------------------------------------------------------------------------
// JSON Import modal
// ---------------------------------------------------------------------------

function openImportModal(): void {
  showImportModal.value = true
}

function closeImportModal(): void {
  showImportModal.value = false
}

async function handleImport(request: ImportCharacterJsonRequest): Promise<void> {
  const result = await store.importCharacterFromJson(request)
  if (result.success) {
    closeImportModal()
  } else {
    // Push server-side validation errors back into the modal
    importModalRef.value?.setErrors(result.errors)
  }
}

// ---------------------------------------------------------------------------
// PDF Import
// ---------------------------------------------------------------------------

function triggerPdfImport(): void {
  pdfImportError.value = null
  pdfFileInput.value?.click()
}

async function handlePdfFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return

  // Reset so the same file can be re-selected
  input.value = ''

  pdfImporting.value = true
  pdfImportError.value = null

  try {
    const formData = new FormData()
    formData.append('file', file)
    formData.append('gameSystem', 'D&D 5e')
    formData.append('ruleset', '2014')
    if (props.campaignId) formData.append('campaignId', props.campaignId)

    const response = await apiClient.post('/api/characters/import/pdf', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })

    const data = response.data as CharacterFieldReviewDto | { isOcrRequired?: boolean }

    // If the backend returned a review DTO, redirect to the review page
    if ('reviewRequired' in data && data.reviewRequired) {
      const reviewData = data as CharacterFieldReviewDto
      await router.push({
        name: 'character-field-review',
        params: { characterId: reviewData.characterId },
        query: {
          unmappedFields: encodeURIComponent(JSON.stringify(reviewData.unmappedFields)),
          warnings: encodeURIComponent(JSON.stringify(reviewData.warnings ?? [])),
        },
      })
      return
    }

    // All fields mapped — character was already persisted; refresh the list
    if (props.campaignId) {
      await store.fetchCharacters(props.campaignId)
    }
  } catch (err: unknown) {
    if (
      err &&
      typeof err === 'object' &&
      'response' in err &&
      err.response &&
      typeof err.response === 'object' &&
      'data' in err.response
    ) {
      const data = err.response.data as { detail?: string; title?: string }
      pdfImportError.value = data.detail ?? data.title ?? 'PDF import failed.'
    } else {
      pdfImportError.value = err instanceof Error ? err.message : 'PDF import failed.'
    }
  } finally {
    pdfImporting.value = false
  }
}
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
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Characters</h1>
        </div>

        <div v-if="!showForm" class="flex items-center gap-2">
          <!-- Hidden PDF file input -->
          <input
            ref="pdfFileInput"
            type="file"
            accept=".pdf,application/pdf"
            class="sr-only"
            aria-label="Select PDF character sheet"
            @change="handlePdfFileChange"
          />

          <!-- Import PDF button -->
          <button
            :disabled="pdfImporting"
            class="inline-flex items-center gap-2 rounded-lg border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-semibold text-gray-300 shadow hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
            @click="triggerPdfImport"
          >
            <svg
              v-if="pdfImporting"
              class="h-4 w-4 animate-spin"
              viewBox="0 0 24 24"
              fill="none"
              aria-hidden="true"
            >
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
            </svg>
            <svg v-else class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path
                fill-rule="evenodd"
                d="M4 4a2 2 0 012-2h4.586A2 2 0 0112 2.586L15.414 6A2 2 0 0116 7.414V16a2 2 0 01-2 2H6a2 2 0 01-2-2V4zm2 6a1 1 0 011-1h6a1 1 0 110 2H7a1 1 0 01-1-1zm1 3a1 1 0 100 2h6a1 1 0 100-2H7z"
                clip-rule="evenodd"
              />
            </svg>
            {{ pdfImporting ? 'Importing…' : 'Import PDF' }}
          </button>

          <!-- Import JSON button -->
          <button
            class="inline-flex items-center gap-2 rounded-lg border border-gray-600 bg-gray-800 px-4 py-2 text-sm font-semibold text-gray-300 shadow hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            @click="openImportModal"
          >
            <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path
                fill-rule="evenodd"
                d="M10 3a.75.75 0 01.75.75v8.614l2.955-3.129a.75.75 0 011.09 1.03l-4.25 4.5a.75.75 0 01-1.09 0l-4.25-4.5a.75.75 0 111.09-1.03L9.25 12.364V3.75A.75.75 0 0110 3z"
                clip-rule="evenodd"
              />
              <path
                d="M3.5 12.75a.75.75 0 00-1.5 0v2.5A2.75 2.75 0 004.75 18h10.5A2.75 2.75 0 0018 15.25v-2.5a.75.75 0 00-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5z"
              />
            </svg>
            Import JSON
          </button>

          <button
            class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            @click="openCreateForm"
          >
            + New Character
          </button>
        </div>
      </div>
    </header>

    <div class="mx-auto max-w-5xl space-y-6 px-6 py-8">
      <!-- Global error banner -->
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

      <!-- PDF import error banner -->
      <div
        v-if="pdfImportError"
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
        <span>PDF import failed: {{ pdfImportError }}</span>
      </div>

      <!-- Create / Edit form panel -->
      <section
        v-if="showForm"
        aria-labelledby="character-form-heading"
        class="rounded-xl border border-gray-800 bg-gray-900 p-6"
      >
        <h2 id="character-form-heading" class="mb-6 text-lg font-semibold text-white">
          {{ editingCharacter ? 'Edit Character' : 'New Character' }}
        </h2>

        <CharacterForm
          :character="editingCharacter ?? undefined"
          :campaign-id="campaignId"
          @submit="handleFormSubmit"
          @cancel="closeForm"
        />
      </section>

      <!-- Character list -->
      <CharacterList @edit="openEditForm" @view="openEditForm" />
    </div>

    <!-- Import JSON modal -->
    <ImportCharacterModal
      ref="importModalRef"
      :open="showImportModal"
      :campaign-id="campaignId"
      @import="handleImport"
      @close="closeImportModal"
    />
  </main>
</template>

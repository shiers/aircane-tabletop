<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useLibraryStore } from '../store'
import { SourceType, ContentVisibility } from '../api'

const store = useLibraryStore()

const fileInput = ref<HTMLInputElement | null>(null)

const form = reactive({
  file: null as File | null,
  title: '',
  sourceType: SourceType.Rules,
  gameSystem: '',
  ruleset: '',
  visibility: ContentVisibility.DMOnly,
})

const localError = ref<string | null>(null)
const submitted = ref(false)

const sourceTypeOptions: { label: string; value: SourceType }[] = [
  { label: 'Rules', value: SourceType.Rules },
  { label: 'Adventure', value: SourceType.Adventure },
  { label: 'Solo Module', value: SourceType.Solo },
  { label: 'Character', value: SourceType.Character },
  { label: 'Homebrew', value: SourceType.Homebrew },
  { label: 'Generated', value: SourceType.Generated },
  { label: 'Unknown', value: SourceType.Unknown },
]

const visibilityOptions: { label: string; value: ContentVisibility }[] = [
  { label: 'DM Only', value: ContentVisibility.DMOnly },
  { label: 'Public', value: ContentVisibility.Public },
  { label: 'Hidden', value: ContentVisibility.Hidden },
]

function onFileChange(event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  form.file = file
  if (file && !form.title) {
    // Pre-fill title from filename (strip extension)
    form.title = file.name.replace(/\.[^.]+$/, '')
  }
}

async function handleSubmit(): Promise<void> {
  localError.value = null

  if (!form.file) {
    localError.value = 'Please select a file to upload.'
    return
  }
  if (!form.title.trim()) {
    localError.value = 'Please enter a title.'
    return
  }

  submitted.value = true
  try {
    await store.uploadDocument({
      file: form.file,
      title: form.title.trim(),
      sourceType: form.sourceType,
      gameSystem: form.gameSystem.trim() || undefined,
      ruleset: form.ruleset.trim() || undefined,
      visibility: form.visibility,
    })
    // Reset form on success
    form.file = null
    form.title = ''
    form.sourceType = SourceType.Rules
    form.gameSystem = ''
    form.ruleset = ''
    form.visibility = ContentVisibility.DMOnly
    if (fileInput.value) fileInput.value.value = ''
  } catch {
    localError.value = store.error ?? 'Upload failed.'
  } finally {
    submitted.value = false
  }
}
</script>

<template>
  <section aria-labelledby="upload-heading" class="rounded-xl border border-gray-800 bg-gray-900 p-6">
    <h2 id="upload-heading" class="mb-4 text-lg font-semibold text-white">
      Upload Document
    </h2>

    <form novalidate @submit.prevent="handleSubmit" class="space-y-4">
      <!-- File input -->
      <div>
        <label for="doc-file" class="mb-1 block text-sm font-medium text-gray-300">
          File <span aria-hidden="true" class="text-red-400">*</span>
        </label>
        <input
          id="doc-file"
          ref="fileInput"
          type="file"
          accept=".pdf,.json,.md,.txt"
          required
          aria-required="true"
          class="block w-full cursor-pointer rounded-lg border border-gray-700 bg-gray-800 text-sm text-gray-300 file:mr-4 file:rounded-l-lg file:border-0 file:bg-gray-700 file:px-4 file:py-2 file:text-sm file:font-medium file:text-gray-200 hover:file:bg-gray-600 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          @change="onFileChange"
        />
      </div>

      <!-- Title -->
      <div>
        <label for="doc-title" class="mb-1 block text-sm font-medium text-gray-300">
          Title <span aria-hidden="true" class="text-red-400">*</span>
        </label>
        <input
          id="doc-title"
          v-model="form.title"
          type="text"
          required
          aria-required="true"
          placeholder="e.g. D&D 5e Player's Handbook"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Source type + Visibility row -->
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label for="doc-source-type" class="mb-1 block text-sm font-medium text-gray-300">
            Source Type
          </label>
          <select
            id="doc-source-type"
            v-model.number="form.sourceType"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          >
            <option v-for="opt in sourceTypeOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
        </div>

        <div>
          <label for="doc-visibility" class="mb-1 block text-sm font-medium text-gray-300">
            Visibility
          </label>
          <select
            id="doc-visibility"
            v-model.number="form.visibility"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          >
            <option v-for="opt in visibilityOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
        </div>
      </div>

      <!-- Game system + Ruleset row -->
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label for="doc-game-system" class="mb-1 block text-sm font-medium text-gray-300">
            Game System
          </label>
          <input
            id="doc-game-system"
            v-model="form.gameSystem"
            type="text"
            placeholder="e.g. D&D 5e"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="doc-ruleset" class="mb-1 block text-sm font-medium text-gray-300">
            Ruleset
          </label>
          <input
            id="doc-ruleset"
            v-model="form.ruleset"
            type="text"
            placeholder="e.g. 2014"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>

      <!-- Error message -->
      <p v-if="localError" role="alert" class="text-sm text-red-400">
        {{ localError }}
      </p>

      <!-- Upload progress bar -->
      <div
        v-if="store.loading && store.uploadProgress > 0"
        role="progressbar"
        :aria-valuenow="store.uploadProgress"
        aria-valuemin="0"
        aria-valuemax="100"
        :aria-label="`Upload progress: ${store.uploadProgress}%`"
        class="overflow-hidden rounded-full bg-gray-700"
      >
        <div
          class="h-2 rounded-full bg-aircane-500 transition-all duration-200"
          :style="{ width: `${store.uploadProgress}%` }"
        />
      </div>

      <!-- Submit button -->
      <button
        type="submit"
        :disabled="store.loading"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <svg
          v-if="store.loading"
          class="h-4 w-4 animate-spin"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
        {{ store.loading ? 'Uploading…' : 'Upload' }}
      </button>
    </form>
  </section>
</template>

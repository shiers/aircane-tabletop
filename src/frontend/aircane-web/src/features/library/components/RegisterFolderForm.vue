<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useLibraryStore } from '../store'
import { SourceType } from '../api'

const store = useLibraryStore()

const showForm = ref(false)
const localError = ref<string | null>(null)
const submitting = ref(false)

const form = reactive({
  displayName: '',
  absolutePath: '',
  defaultSourceType: SourceType.Rules,
  defaultGameSystem: '',
  defaultRuleset: '',
})

const sourceTypeOptions: { label: string; value: SourceType }[] = [
  { label: 'Rules', value: SourceType.Rules },
  { label: 'Adventure', value: SourceType.Adventure },
  { label: 'Solo Module', value: SourceType.Solo },
  { label: 'Character', value: SourceType.Character },
  { label: 'Homebrew', value: SourceType.Homebrew },
  { label: 'Unknown', value: SourceType.Unknown },
]

function resetForm(): void {
  form.displayName = ''
  form.absolutePath = ''
  form.defaultSourceType = SourceType.Rules
  form.defaultGameSystem = ''
  form.defaultRuleset = ''
  localError.value = null
}

function handleCancel(): void {
  resetForm()
  showForm.value = false
}

async function handleSubmit(): Promise<void> {
  localError.value = null

  if (!form.displayName.trim()) {
    localError.value = 'Please enter a display name.'
    return
  }
  if (!form.absolutePath.trim()) {
    localError.value = 'Please enter the absolute folder path.'
    return
  }

  submitting.value = true
  try {
    await store.registerFolder({
      displayName: form.displayName.trim(),
      absolutePath: form.absolutePath.trim(),
      defaultSourceType: form.defaultSourceType,
      defaultGameSystem: form.defaultGameSystem.trim() || undefined,
      defaultRuleset: form.defaultRuleset.trim() || undefined,
    })
    resetForm()
    showForm.value = false
  } catch {
    localError.value = store.foldersError ?? 'Failed to register folder.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div>
    <!-- Toggle button when form is hidden -->
    <button
      v-if="!showForm"
      type="button"
      class="inline-flex items-center gap-2 rounded-lg border border-dashed border-gray-600 px-4 py-2.5 text-sm font-medium text-gray-300 hover:border-aircane-500 hover:text-aircane-400 focus:outline-none focus:ring-2 focus:ring-aircane-500"
      @click="showForm = true"
    >
      <svg
        class="h-4 w-4"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          d="M10.75 4.75a.75.75 0 00-1.5 0v4.5h-4.5a.75.75 0 000 1.5h4.5v4.5a.75.75 0 001.5 0v-4.5h4.5a.75.75 0 000-1.5h-4.5v-4.5z"
        />
      </svg>
      Register Folder
    </button>

    <!-- Registration form -->
    <section
      v-else
      aria-labelledby="register-folder-heading"
      class="rounded-xl border border-gray-700 bg-gray-900 p-5"
    >
      <h3 id="register-folder-heading" class="mb-4 text-base font-semibold text-white">
        Register Folder
      </h3>

      <form novalidate class="space-y-4" @submit.prevent="handleSubmit">
        <!-- Display name -->
        <div>
          <label for="folder-display-name" class="mb-1 block text-sm font-medium text-gray-300">
            Display Name <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="folder-display-name"
            v-model="form.displayName"
            type="text"
            required
            aria-required="true"
            placeholder="e.g. D&D 5e Rulebooks"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <!-- Absolute path -->
        <div>
          <label for="folder-path" class="mb-1 block text-sm font-medium text-gray-300">
            Folder Path <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="folder-path"
            v-model="form.absolutePath"
            type="text"
            required
            aria-required="true"
            placeholder="e.g. /home/user/rpg-books or C:\RPG Books"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
          <p class="mt-1 text-xs text-gray-500">
            Enter the absolute path to the folder on the server's filesystem.
          </p>
        </div>

        <!-- Default source type -->
        <div>
          <label for="folder-source-type" class="mb-1 block text-sm font-medium text-gray-300">
            Default Source Type
          </label>
          <select
            id="folder-source-type"
            v-model.number="form.defaultSourceType"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          >
            <option v-for="opt in sourceTypeOptions" :key="opt.value" :value="opt.value">
              {{ opt.label }}
            </option>
          </select>
        </div>

        <!-- Game system + Ruleset row -->
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label for="folder-game-system" class="mb-1 block text-sm font-medium text-gray-300">
              Default Game System
            </label>
            <input
              id="folder-game-system"
              v-model="form.defaultGameSystem"
              type="text"
              placeholder="e.g. D&D 5e"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label for="folder-ruleset" class="mb-1 block text-sm font-medium text-gray-300">
              Default Ruleset
            </label>
            <input
              id="folder-ruleset"
              v-model="form.defaultRuleset"
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

        <!-- Actions -->
        <div class="flex items-center gap-3">
          <button
            type="submit"
            :disabled="submitting"
            class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <svg
              v-if="submitting"
              class="h-4 w-4 animate-spin"
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
            {{ submitting ? 'Registering…' : 'Register Folder' }}
          </button>
          <button
            type="button"
            :disabled="submitting"
            class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500 disabled:opacity-50"
            @click="handleCancel"
          >
            Cancel
          </button>
        </div>
      </form>
    </section>
  </div>
</template>

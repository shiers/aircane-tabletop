<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { browseFolders, type BrowseDirectoryEntry } from '../api'

const props = defineProps<{
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  close: []
}>()

const currentPath = ref<string | null>(null)
const parentPath = ref<string | null>(null)
const directories = ref<BrowseDirectoryEntry[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

async function loadDirectory(path?: string): Promise<void> {
  if (loading.value) return // prevent duplicate requests
  loading.value = true
  error.value = null
  try {
    const result = await browseFolders(path)
    currentPath.value = result.currentPath
    parentPath.value = result.parentPath
    directories.value = result.directories
  } catch (err: unknown) {
    if (err && typeof err === 'object' && 'code' in err && err.code === 'ECONNABORTED') {
      error.value = 'Request timed out. Is the backend running?'
    } else if (err && typeof err === 'object' && 'response' in err) {
      const response = (err as { response?: { data?: { detail?: string } } }).response
      error.value = response?.data?.detail ?? 'Failed to browse directory.'
    } else {
      error.value = err instanceof Error ? err.message : 'Failed to browse directory.'
    }
  } finally {
    loading.value = false
  }
}

function navigateTo(entry: BrowseDirectoryEntry): void {
  loadDirectory(entry.fullPath)
}

function navigateUp(): void {
  if (parentPath.value) {
    loadDirectory(parentPath.value)
  } else {
    // Go back to roots
    loadDirectory(undefined)
  }
}

function selectCurrent(): void {
  if (currentPath.value) {
    emit('update:modelValue', currentPath.value)
    emit('close')
  }
}

onMounted(() => {
  // If there's already a value, try to open that directory
  if (props.modelValue) {
    loadDirectory(props.modelValue)
  } else {
    loadDirectory(undefined)
  }
})
</script>

<template>
  <div class="rounded-lg border border-gray-600 bg-gray-800 p-4">
    <!-- Header with current path and actions -->
    <div class="mb-3 flex items-center justify-between gap-2">
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-medium text-gray-200">
          {{ currentPath ?? 'Select a drive' }}
        </p>
      </div>
      <div class="flex items-center gap-2">
        <button
          v-if="currentPath"
          type="button"
          class="rounded bg-aircane-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="selectCurrent"
        >
          Select
        </button>
        <button
          type="button"
          class="rounded px-3 py-1.5 text-xs font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
          @click="emit('close')"
        >
          Cancel
        </button>
      </div>
    </div>

    <!-- Navigation -->
    <div
      class="max-h-60 overflow-y-auto rounded border border-gray-700 bg-gray-900"
      role="listbox"
      aria-label="Directory listing"
    >
      <!-- Loading state -->
      <div v-if="loading" class="flex items-center justify-center py-6">
        <svg
          class="h-5 w-5 animate-spin text-aircane-400"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
        <span class="sr-only">Loading directories…</span>
      </div>

      <!-- Error state -->
      <div v-else-if="error" class="px-3 py-4 text-center text-sm text-red-400">
        {{ error }}
      </div>

      <!-- Directory list -->
      <div v-else>
        <!-- Up / parent directory -->
        <button
          v-if="currentPath"
          type="button"
          class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-gray-300 hover:bg-gray-700 focus:bg-gray-700 focus:outline-none"
          role="option"
          @click="navigateUp"
        >
          <svg class="h-4 w-4 shrink-0 text-gray-500" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
            <path fill-rule="evenodd" d="M17 10a.75.75 0 01-.75.75H5.612l4.158 3.96a.75.75 0 11-1.04 1.08l-5.5-5.25a.75.75 0 010-1.08l5.5-5.25a.75.75 0 111.04 1.08L5.612 9.25H16.25A.75.75 0 0117 10z" clip-rule="evenodd" />
          </svg>
          <span>..</span>
        </button>

        <!-- Empty state -->
        <p
          v-if="directories.length === 0 && !currentPath"
          class="px-3 py-4 text-center text-sm text-gray-500"
        >
          No drives found.
        </p>
        <p
          v-else-if="directories.length === 0"
          class="px-3 py-4 text-center text-sm text-gray-500"
        >
          No subdirectories.
        </p>

        <!-- Directory entries -->
        <button
          v-for="dir in directories"
          :key="dir.fullPath"
          type="button"
          class="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-gray-200 hover:bg-gray-700 focus:bg-gray-700 focus:outline-none"
          role="option"
          @click="navigateTo(dir)"
        >
          <svg class="h-4 w-4 shrink-0 text-yellow-500" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
            <path d="M3.75 3A1.75 1.75 0 002 4.75v3.26a3.235 3.235 0 011.75-.51h12.5c.644 0 1.245.188 1.75.51V6.75A1.75 1.75 0 0016.25 5h-4.836a.25.25 0 01-.177-.073L9.823 3.513A1.75 1.75 0 008.586 3H3.75zM3.75 9A1.75 1.75 0 002 10.75v4.5c0 .966.784 1.75 1.75 1.75h12.5A1.75 1.75 0 0018 15.25v-4.5A1.75 1.75 0 0016.25 9H3.75z" />
          </svg>
          <span class="truncate">{{ dir.name }}</span>
        </button>
      </div>
    </div>
  </div>
</template>

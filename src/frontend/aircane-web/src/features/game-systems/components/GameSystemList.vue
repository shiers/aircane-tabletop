<script setup lang="ts">
import { onMounted, computed, ref } from 'vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'
import type { GameSystemDefinitionSummary } from '../types'

const emit = defineEmits<{
  (e: 'select', id: string): void
  (e: 'edit', id: string): void
  (e: 'create'): void
}>()

const store = useGameSystemStore()
const definitions = computed(() => store.definitions)
const importInput = ref<HTMLInputElement | null>(null)

onMounted(() => {
  store.fetchAll()
})

async function handleExport(def: GameSystemDefinitionSummary): Promise<void> {
  await store.exportFile(def.id, 'json')
}

async function handleDeactivate(def: GameSystemDefinitionSummary): Promise<void> {
  if (!confirm(`Deactivate "${def.name}"? This cannot be undone if campaigns reference it.`)) return
  await store.deactivate(def.id)
}

async function handleImport(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  await store.importFile(file)
  input.value = ''
}
</script>

<template>
  <div class="space-y-4">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <h2 class="text-lg font-semibold text-gray-100">Game System Definitions</h2>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="emit('create')"
        >
          Create New
        </button>
        <button
          type="button"
          class="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-300 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="importInput?.click()"
        >
          Import
        </button>
        <input
          ref="importInput"
          type="file"
          accept=".json,.yaml,.yml"
          class="hidden"
          @change="handleImport"
        />
      </div>
    </div>

    <!-- Loading -->
    <p v-if="store.loading" class="text-sm text-gray-400">Loading definitions…</p>

    <!-- Error -->
    <p v-if="store.error" role="alert" class="text-sm text-red-400">{{ store.error }}</p>

    <!-- Empty state -->
    <p v-if="!store.loading && definitions.length === 0" class="text-sm text-gray-500">
      No game system definitions found.
    </p>

    <!-- List -->
    <div v-if="definitions.length > 0" class="space-y-2">
      <div
        v-for="def in definitions"
        :key="def.id"
        class="flex items-center justify-between rounded-lg border border-gray-700 bg-gray-800 p-4"
      >
        <button
          type="button"
          class="flex-1 text-left focus:outline-none"
          @click="emit('select', def.id)"
        >
          <div class="flex items-center gap-3">
            <div>
              <p class="text-sm font-medium text-gray-100">{{ def.name }}</p>
              <p class="text-xs text-gray-400">
                v{{ def.version }}
                <span v-if="def.genre"> · {{ def.genre }}</span>
                <span v-if="def.isBuiltIn" class="ml-2 rounded bg-gray-700 px-1.5 py-0.5 text-xs text-gray-300">Built-in</span>
              </p>
            </div>
          </div>
          <p v-if="def.description" class="mt-1 text-xs text-gray-500 line-clamp-1">{{ def.description }}</p>
        </button>

        <div class="flex items-center gap-1 ml-4">
          <button
            type="button"
            class="rounded px-2 py-1 text-xs text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-1 focus:ring-aircane-400"
            title="Edit"
            @click="emit('edit', def.id)"
          >
            Edit
          </button>
          <button
            type="button"
            class="rounded px-2 py-1 text-xs text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-1 focus:ring-aircane-400"
            title="Export"
            @click="handleExport(def)"
          >
            Export
          </button>
          <button
            v-if="!def.isBuiltIn"
            type="button"
            class="rounded px-2 py-1 text-xs text-red-400 hover:text-red-300 focus:outline-none focus:ring-1 focus:ring-red-400"
            title="Deactivate"
            @click="handleDeactivate(def)"
          >
            Deactivate
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

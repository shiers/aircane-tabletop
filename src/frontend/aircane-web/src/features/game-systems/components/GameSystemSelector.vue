<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'

const props = defineProps<{
  /** Currently selected game system definition ID. */
  modelValue: string | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | null): void
}>()

const store = useGameSystemStore()
const definitions = computed(() => store.definitions)

const selectedDefinition = computed(() =>
  definitions.value.find((d) => d.id === props.modelValue) ?? null,
)

onMounted(() => {
  if (definitions.value.length === 0) {
    store.fetchAll()
  }
})

function handleChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  emit('update:modelValue', value || null)
}
</script>

<template>
  <div class="space-y-2">
    <label for="game-system-selector" class="mb-1 block text-sm font-medium text-gray-300">
      Game System <span aria-hidden="true" class="text-red-400">*</span>
    </label>
    <select
      id="game-system-selector"
      :value="modelValue ?? ''"
      class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
      @change="handleChange"
    >
      <option value="">Select a game system…</option>
      <option
        v-for="def in definitions"
        :key="def.id"
        :value="def.id"
      >
        {{ def.name }} (v{{ def.version }})
      </option>
    </select>

    <!-- Summary of selected definition -->
    <div v-if="selectedDefinition" class="rounded-lg border border-gray-700 bg-gray-800 p-3">
      <p class="text-sm font-medium text-gray-200">{{ selectedDefinition.name }}</p>
      <p class="text-xs text-gray-400">
        v{{ selectedDefinition.version }}
        <span v-if="selectedDefinition.genre"> · {{ selectedDefinition.genre }}</span>
        <span v-if="selectedDefinition.publisher"> · {{ selectedDefinition.publisher }}</span>
      </p>
      <p v-if="selectedDefinition.description" class="mt-1 text-xs text-gray-500">
        {{ selectedDefinition.description }}
      </p>
    </div>
  </div>
</template>

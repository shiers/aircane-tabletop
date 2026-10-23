<script setup lang="ts">
import { onMounted, computed } from 'vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'

/** The select's value for "Not listed"; no definition id can look like it. */
const NOT_LISTED = '__not-listed__'

const props = withDefaults(
  defineProps<{
    /** Currently selected game system definition ID. */
    modelValue: string | null
    /**
     * Offers a "Not listed" option, for a game system Aircane has no definition for. Choosing it emits a null
     * `modelValue` and `update:notListed` with true; choosing a definition emits its id and false.
     */
    allowNotListed?: boolean
    /** Whether "Not listed" is the current choice (only meaningful with `allowNotListed`). */
    notListed?: boolean
    disabled?: boolean
  }>(),
  { allowNotListed: false, notListed: false, disabled: false },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | null): void
  (e: 'update:notListed', value: boolean): void
}>()

const store = useGameSystemStore()
const definitions = computed(() => store.definitions)

const selectedDefinition = computed(() =>
  definitions.value.find((d) => d.id === props.modelValue) ?? null,
)

// A parent that loads the definitions itself (so it can act on them) is already doing it: do not ask twice.
onMounted(() => {
  if (definitions.value.length === 0 && !store.loading) {
    store.fetchAll()
  }
})

const selectValue = computed(() => (props.allowNotListed && props.notListed ? NOT_LISTED : (props.modelValue ?? '')))

const placeholder = computed(() =>
  store.loading && definitions.value.length === 0 ? 'Loading game systems…' : 'Select a game system…',
)

function handleChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value
  if (value === NOT_LISTED) {
    emit('update:modelValue', null)
    emit('update:notListed', true)
    return
  }
  emit('update:modelValue', value || null)
  emit('update:notListed', false)
}
</script>

<template>
  <div class="space-y-2">
    <label for="game-system-selector" class="mb-1 block text-sm font-medium text-gray-300">
      Game System <span aria-hidden="true" class="text-red-400">*</span>
    </label>
    <select
      id="game-system-selector"
      :value="selectValue"
      :disabled="disabled"
      class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
      @change="handleChange"
    >
      <!-- With "Not listed" on offer there is always an answer, so "nothing chosen" cannot be picked again. -->
      <option value="" :disabled="allowNotListed">{{ placeholder }}</option>
      <option
        v-for="def in definitions"
        :key="def.id"
        :value="def.id"
      >
        {{ def.name }} (v{{ def.version }})
      </option>
      <option v-if="allowNotListed" :value="NOT_LISTED">Not listed</option>
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

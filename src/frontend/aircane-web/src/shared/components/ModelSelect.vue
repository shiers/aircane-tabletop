<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    id?: string
    modelValue: string
    models: string[]
    loading?: boolean
    placeholder?: string
  }>(),
  {
    id: undefined,
    loading: false,
    placeholder: '',
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

// When the provider returns a model list we render a plain dropdown of those models — the list is
// fetched live from the provider, so there is no "Custom…" escape hatch. When no list is available
// (no API key saved yet, provider unreachable, or a provider whose models can't be enumerated such
// as Azure deployment names), we fall back to a free-text input so the host can still enter a value.

// If the saved value isn't in the fetched list (e.g. a previously-saved model the API no longer
// returns), include it as an option so the selection stays visible and valid.
const options = computed<string[]>(() => {
  if (props.modelValue && !props.models.includes(props.modelValue)) {
    return [props.modelValue, ...props.models]
  }
  return props.models
})

function onSelectChange(event: Event) {
  emit('update:modelValue', (event.target as HTMLSelectElement).value)
}

function onTextInput(event: Event) {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

const fieldClasses =
  'w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500'
</script>

<template>
  <div class="space-y-2">
    <!-- Dropdown when the provider returned a fetched model list -->
    <select
      v-if="models.length > 0"
      :id="id"
      :value="modelValue"
      :class="fieldClasses"
      @change="onSelectChange"
    >
      <option v-for="model in options" :key="model" :value="model">{{ model }}</option>
    </select>

    <!-- Free-text entry when no model list is available -->
    <input
      v-else
      :id="id"
      :value="modelValue"
      type="text"
      :placeholder="placeholder"
      :class="fieldClasses"
      @input="onTextInput"
    />

    <p v-if="loading" class="text-xs text-gray-500">Loading available models...</p>
    <p v-else-if="models.length === 0" class="text-xs text-gray-500">
      No model list available - enter the model name manually.
    </p>
  </div>
</template>

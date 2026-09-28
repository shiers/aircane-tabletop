<script setup lang="ts">
import { computed, ref, watch } from 'vue'

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

const CUSTOM = '__custom__'

// True when the current value isn't one of the known models, so the user is
// entering a custom model name.
const isCustom = ref(false)

function syncCustomFlag() {
  if (props.models.length === 0) {
    isCustom.value = false
    return
  }
  isCustom.value = props.modelValue !== '' && !props.models.includes(props.modelValue)
}

watch(
  () => [props.models, props.modelValue],
  () => syncCustomFlag(),
  { immediate: true, deep: true },
)

// The value bound to the <select>: either a real model or the sentinel.
const selectValue = computed<string>({
  get() {
    if (isCustom.value) return CUSTOM
    return props.modelValue
  },
  set(value: string) {
    if (value === CUSTOM) {
      isCustom.value = true
      // Keep whatever the user had; clear only if it matched a known model.
      if (props.models.includes(props.modelValue)) {
        emit('update:modelValue', '')
      }
      return
    }
    isCustom.value = false
    emit('update:modelValue', value)
  },
})

function onTextInput(event: Event) {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

const selectClasses =
  'w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500'
</script>

<template>
  <div class="space-y-2">
    <!-- Dropdown when we have a model list from the provider -->
    <select v-if="models.length > 0" :id="id" v-model="selectValue" :class="selectClasses">
      <option v-for="model in models" :key="model" :value="model">{{ model }}</option>
      <option :value="CUSTOM">Custom…</option>
    </select>

    <!-- Free-text entry: either no list available, or user picked "Custom…" -->
    <input
      v-if="models.length === 0 || isCustom"
      :id="models.length === 0 ? id : undefined"
      :value="modelValue"
      type="text"
      :placeholder="placeholder"
      class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
      @input="onTextInput"
    />

    <p v-if="loading" class="text-xs text-gray-500">Loading available models...</p>
    <p v-else-if="models.length === 0" class="text-xs text-gray-500">
      No model list available - enter the model name manually.
    </p>
  </div>
</template>

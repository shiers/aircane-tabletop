<script setup lang="ts">
// A native <select> styled with a gold chevron, for choosing from a fixed list
// of model options. Complements ModelSelect.vue (which also handles the
// no-models free-text fallback); this component is the always-a-dropdown form.
withDefaults(
  defineProps<{
    modelValue: string
    options: string[]
    id?: string
  }>(),
  {
    id: undefined,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

function onChange(event: Event) {
  emit('update:modelValue', (event.target as HTMLSelectElement).value)
}
</script>

<template>
  <div class="model-select-field relative">
    <select
      :id="id"
      :value="modelValue"
      class="w-full appearance-none rounded-md border border-gray-700 bg-surface-850 px-3 py-2 pr-10 text-gray-100 focus:ring-2 focus:ring-aircane-500"
      @change="onChange"
    >
      <option v-for="option in options" :key="option" :value="option">{{ option }}</option>
    </select>
    <!-- Gold chevron -->
    <svg
      class="pointer-events-none absolute inset-y-0 right-0 my-auto mr-3 h-4 w-4"
      viewBox="0 0 24 24"
      fill="none"
      stroke="var(--color-gold)"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path d="m6 9 6 6 6-6" />
    </svg>
  </div>
</template>

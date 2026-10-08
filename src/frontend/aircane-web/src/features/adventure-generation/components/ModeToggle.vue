<script setup lang="ts">
defineProps<{
  modelValue: 'solo' | 'group'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': ['solo' | 'group']
}>()

function select(value: 'solo' | 'group') {
  emit('update:modelValue', value)
}
</script>

<template>
  <div class="flex gap-4" role="radiogroup" aria-label="Adventure Mode">
    <button
      type="button"
      role="radio"
      :aria-checked="modelValue === 'solo'"
      :disabled="disabled"
      class="flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50"
      :class="
        modelValue === 'solo'
          ? 'border-aircane-500 bg-aircane-950 text-aircane-300 shadow-[var(--glow-purple)]'
          : 'border-gray-700 bg-surface-850 text-gray-400 hover:border-gray-600'
      "
      @click="select('solo')"
    >
      <span aria-hidden="true">👤</span>
      <span>Solo</span>
    </button>
    <button
      type="button"
      role="radio"
      :aria-checked="modelValue === 'group'"
      :disabled="disabled"
      class="flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50"
      :class="
        modelValue === 'group'
          ? 'border-aircane-500 bg-aircane-950 text-aircane-300 shadow-[var(--glow-purple)]'
          : 'border-gray-700 bg-surface-850 text-gray-400 hover:border-gray-600'
      "
      @click="select('group')"
    >
      <span aria-hidden="true">👥</span>
      <span>Group</span>
    </button>
  </div>
</template>

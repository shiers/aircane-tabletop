<script setup lang="ts">
defineProps<{
  id?: string
  modelValue: string | null
  options: { id: string; name: string }[]
  disabled?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | null): void
}>()

function onChange(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  emit('update:modelValue', value === '' ? null : value)
}
</script>

<template>
  <div
    class="relative flex items-center rounded-lg bg-cover bg-center"
    style="background-image: url('/assets/rules/game-system-field-art.png')"
  >
    <!-- Dice icon (decorative) -->
    <img
      src="/assets/ui/icon-dice.png"
      alt=""
      aria-hidden="true"
      class="pointer-events-none ml-3 h-5 w-5 flex-shrink-0"
      style="object-fit: contain"
    />

    <!-- Native select, transparent over the field art -->
    <select
      :id="id"
      :value="modelValue ?? ''"
      :disabled="disabled"
      class="w-full appearance-none bg-transparent px-3 py-2 text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-600/20 disabled:cursor-not-allowed disabled:opacity-50"
      @change="onChange"
    >
      <option value="">Any game system</option>
      <option v-for="opt in options" :key="opt.id" :value="opt.id">{{ opt.name }}</option>
    </select>

    <!-- Gold chevron -->
    <span
      class="pointer-events-none absolute right-3 flex-shrink-0"
      aria-hidden="true"
      style="color: var(--color-gold)"
    >
      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M6 8l4 4 4-4" />
      </svg>
    </span>
  </div>
</template>

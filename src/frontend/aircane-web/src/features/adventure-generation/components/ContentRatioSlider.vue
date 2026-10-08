<script setup lang="ts">
defineProps<{
  combat: number
  exploration: number
  roleplay: number
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:combat': [number]
  'update:exploration': [number]
  'update:roleplay': [number]
}>()

function onInput(event: Event, kind: 'combat' | 'exploration' | 'roleplay') {
  const value = Number((event.target as HTMLInputElement).value)
  if (kind === 'combat') emit('update:combat', value)
  else if (kind === 'exploration') emit('update:exploration', value)
  else emit('update:roleplay', value)
}
</script>

<template>
  <div class="space-y-4">
    <div>
      <div class="flex items-center justify-between mb-1">
        <label for="combat-ratio" class="flex items-center gap-2 text-sm text-gray-400">
          <span aria-hidden="true">⚔️</span>
          <span>Combat</span>
        </label>
        <span class="text-sm text-gray-300 font-mono">{{ combat }}%</span>
      </div>
      <input
        id="combat-ratio"
        :value="combat"
        type="range"
        min="0"
        max="100"
        step="5"
        class="w-full accent-aircane-500"
        :disabled="disabled"
        @input="onInput($event, 'combat')"
      />
    </div>

    <div>
      <div class="flex items-center justify-between mb-1">
        <label for="exploration-ratio" class="flex items-center gap-2 text-sm text-gray-400">
          <span aria-hidden="true">🧭</span>
          <span>Exploration</span>
        </label>
        <span class="text-sm text-gray-300 font-mono">{{ exploration }}%</span>
      </div>
      <input
        id="exploration-ratio"
        :value="exploration"
        type="range"
        min="0"
        max="100"
        step="5"
        class="w-full accent-aircane-500"
        :disabled="disabled"
        @input="onInput($event, 'exploration')"
      />
    </div>

    <div>
      <div class="flex items-center justify-between mb-1">
        <label for="roleplay-ratio" class="flex items-center gap-2 text-sm text-gray-400">
          <span aria-hidden="true">🎭</span>
          <span>Roleplay</span>
        </label>
        <span class="text-sm text-gray-300 font-mono">{{ roleplay }}%</span>
      </div>
      <input
        id="roleplay-ratio"
        :value="roleplay"
        type="range"
        min="0"
        max="100"
        step="5"
        class="w-full accent-aircane-500"
        :disabled="disabled"
        @input="onInput($event, 'roleplay')"
      />
    </div>
  </div>
</template>

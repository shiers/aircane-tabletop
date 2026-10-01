<script setup lang="ts">
import { computed } from 'vue'
import type { ActiveCondition } from '../types'

const props = defineProps<{
  /** Active conditions on the character/participant. */
  conditions: ActiveCondition[]
  /** Whether freeform text conditions are allowed (no defined set). */
  freeform?: boolean
}>()

const emit = defineEmits<{
  (e: 'remove', conditionName: string): void
  (e: 'addFreeform', text: string): void
}>()

const hasConditions = computed(() => props.conditions.length > 0)

function durationLabel(condition: ActiveCondition): string {
  if (condition.roundsRemaining != null) {
    return `${condition.roundsRemaining} round${condition.roundsRemaining !== 1 ? 's' : ''} remaining`
  }
  if (condition.durationType === 'until_save') return 'Until save'
  if (condition.durationType === 'until_rest') return 'Until rest'
  if (condition.durationType === 'until_action') return condition.endCondition ?? 'Until action'
  if (condition.durationType === 'permanent') return 'Permanent'
  return ''
}

function handleAddFreeform(): void {
  const text = prompt('Enter condition name:')
  if (text?.trim()) {
    emit('addFreeform', text.trim())
  }
}
</script>

<template>
  <div class="space-y-2">
    <div class="flex items-center justify-between">
      <h4 class="text-sm font-semibold text-gray-200">Conditions</h4>
      <button
        v-if="freeform"
        type="button"
        class="rounded px-2 py-1 text-xs text-aircane-400 hover:text-aircane-300 focus:outline-none focus:ring-1 focus:ring-aircane-400"
        @click="handleAddFreeform"
      >
        + Add
      </button>
    </div>

    <!-- Empty state -->
    <p v-if="!hasConditions" class="text-xs text-gray-500">No active conditions.</p>

    <!-- Condition list -->
    <div v-if="hasConditions" class="space-y-1">
      <div
        v-for="condition in conditions"
        :key="condition.name"
        class="flex items-start justify-between rounded border border-gray-700 bg-gray-900 px-3 py-2"
      >
        <div class="space-y-0.5">
          <p class="text-sm font-medium text-gray-200">{{ condition.name }}</p>
          <p v-if="condition.description" class="text-xs text-gray-400">{{ condition.description }}</p>
          <p v-if="durationLabel(condition)" class="text-xs text-yellow-400">
            {{ durationLabel(condition) }}
          </p>
        </div>
        <button
          type="button"
          class="ml-2 rounded px-1.5 py-0.5 text-xs text-red-400 hover:text-red-300 focus:outline-none focus:ring-1 focus:ring-red-400"
          title="Remove condition"
          @click="emit('remove', condition.name)"
        >
          ✕
        </button>
      </div>
    </div>
  </div>
</template>

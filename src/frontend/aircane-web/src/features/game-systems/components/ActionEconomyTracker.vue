<script setup lang="ts">
import { computed } from 'vue'
import type { ActionBudget, ActionSlotState } from '../types'

const props = defineProps<{
  /** The current action budget for the active participant. */
  budget: ActionBudget | null
}>()

const emit = defineEmits<{
  (e: 'consume', slotName: string): void
}>()

const isFreeform = computed(() => !props.budget || props.budget.type === 'freeform')
const isActionPoints = computed(() => props.budget?.type === 'action_points')
const isMultiActionPenalty = computed(() => props.budget?.type === 'multi_action_penalty')

const slots = computed<ActionSlotState[]>(() => props.budget?.slots ?? [])

function getSlotDots(slot: ActionSlotState): { filled: number; empty: number } {
  if (slot.total < 0) return { filled: 0, empty: 0 } // unlimited (e.g. free actions)
  return {
    filled: slot.total - slot.remaining,
    empty: slot.remaining,
  }
}

function handleConsume(slotName: string): void {
  emit('consume', slotName)
}
</script>

<template>
  <!-- Hidden for freeform systems -->
  <div v-if="!isFreeform && budget" class="space-y-3 rounded-lg border border-gray-700 bg-gray-800 p-4">
    <h4 class="text-sm font-semibold text-gray-200">Action Economy</h4>

    <!-- Action Points display -->
    <div v-if="isActionPoints" class="space-y-2">
      <div class="flex items-center gap-2">
        <span class="text-xs text-gray-400">Action Points:</span>
        <span class="text-sm font-bold text-aircane-400">
          {{ budget.remainingPoints ?? 0 }} / {{ budget.totalPoints ?? 0 }}
        </span>
      </div>
      <div class="h-2 w-full rounded-full bg-gray-700">
        <div
          class="h-2 rounded-full bg-aircane-500 transition-all"
          :style="{ width: `${((budget.remainingPoints ?? 0) / (budget.totalPoints ?? 1)) * 100}%` }"
        />
      </div>
    </div>

    <!-- Multi-action penalty display -->
    <div v-else-if="isMultiActionPenalty" class="space-y-2">
      <div v-for="slot in slots" :key="slot.name" class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="text-xs text-gray-300">{{ slot.label }}</span>
          <span v-if="slot.total - slot.remaining > 0" class="text-xs text-yellow-400">
            (MAP -{{ (slot.total - slot.remaining) * 5 }})
          </span>
        </div>
        <button
          type="button"
          :disabled="slot.remaining <= 0"
          class="rounded px-2 py-1 text-xs text-aircane-400 hover:text-aircane-300 disabled:text-gray-600 disabled:cursor-not-allowed focus:outline-none focus:ring-1 focus:ring-aircane-400"
          @click="handleConsume(slot.name)"
        >
          Use
        </button>
      </div>
    </div>

    <!-- Named slots display (D&D 5e style) -->
    <div v-else class="space-y-2">
      <div v-for="slot in slots" :key="slot.name" class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="text-xs text-gray-300">{{ slot.label }}</span>
          <!-- Dot indicators for limited slots -->
          <div v-if="slot.total > 0" class="flex gap-0.5">
            <span
              v-for="n in getSlotDots(slot).filled"
              :key="`filled-${n}`"
              class="inline-block h-2.5 w-2.5 rounded-full bg-gray-600"
              aria-label="Used"
            />
            <span
              v-for="n in getSlotDots(slot).empty"
              :key="`empty-${n}`"
              class="inline-block h-2.5 w-2.5 rounded-full bg-aircane-500"
              aria-label="Available"
            />
          </div>
          <span v-else class="text-xs text-gray-500">∞</span>
        </div>
        <button
          type="button"
          :disabled="slot.total > 0 && slot.remaining <= 0"
          class="rounded px-2 py-1 text-xs text-aircane-400 hover:text-aircane-300 disabled:text-gray-600 disabled:cursor-not-allowed focus:outline-none focus:ring-1 focus:ring-aircane-400"
          @click="handleConsume(slot.name)"
        >
          Use
        </button>
      </div>
    </div>
  </div>
</template>

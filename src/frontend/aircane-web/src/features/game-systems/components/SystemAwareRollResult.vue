<script setup lang="ts">
import { computed } from 'vue'
import type { SystemAwareRollResult } from '../types'

const props = defineProps<{
  /** The roll result to display. */
  roll: SystemAwareRollResult
}>()

const isPoolSystem = computed(() => props.roll.conventionType === 'dice_pool_success')
const isThresholdSystem = computed(() => props.roll.conventionType === 'fixed_dice_threshold')
const hasOutcomeTier = computed(() => !!props.roll.outcomeTier)

const outcomeTierClass = computed(() => {
  const tier = props.roll.outcomeTier?.toLowerCase()
  if (!tier) return 'text-gray-300'
  if (tier.includes('critical') && tier.includes('success')) return 'text-yellow-400'
  if (tier.includes('success') || tier.includes('strong') || tier.includes('hit')) return 'text-green-400'
  if (tier.includes('mixed') || tier.includes('partial') || tier.includes('weak')) return 'text-yellow-400'
  if (tier.includes('critical') && tier.includes('fail')) return 'text-red-500'
  if (tier.includes('fail') || tier.includes('miss')) return 'text-red-400'
  return 'text-gray-300'
})
</script>

<template>
  <div class="rounded-lg border border-gray-700 bg-gray-800 p-3 space-y-2">
    <!-- Formula / context -->
    <div class="flex items-center justify-between">
      <p v-if="roll.formula" class="text-xs text-gray-400 font-mono">{{ roll.formula }}</p>
      <p v-if="roll.context" class="text-xs text-gray-500">{{ roll.context }}</p>
    </div>

    <!-- Individual die results -->
    <div class="flex flex-wrap gap-1">
      <span
        v-for="(die, idx) in roll.dieResults"
        :key="idx"
        class="inline-flex h-7 w-7 items-center justify-center rounded border border-gray-600 bg-gray-900 text-xs font-medium text-gray-200"
      >
        {{ die }}
      </span>
      <span v-if="roll.modifier !== 0" class="inline-flex items-center text-xs text-gray-400">
        {{ roll.modifier > 0 ? '+' : '' }}{{ roll.modifier }}
      </span>
    </div>

    <!-- Total / Success count -->
    <div class="flex items-center gap-3">
      <!-- Pool system: show success count -->
      <div v-if="isPoolSystem && roll.successCount != null">
        <p class="text-xs text-gray-400">Successes</p>
        <p class="text-2xl font-bold text-aircane-400">{{ roll.successCount }}</p>
      </div>

      <!-- Standard total -->
      <div v-else>
        <p class="text-xs text-gray-400">Total</p>
        <p class="text-2xl font-bold text-aircane-400">{{ roll.total }}</p>
      </div>

      <!-- Outcome tier -->
      <div v-if="hasOutcomeTier">
        <p class="text-xs text-gray-400">Outcome</p>
        <p class="text-sm font-semibold" :class="outcomeTierClass">
          {{ roll.outcomeTier }}
        </p>
      </div>
    </div>

    <!-- Threshold bands (PbtA-style) -->
    <div v-if="isThresholdSystem && roll.thresholdBands" class="flex gap-1">
      <span
        v-for="band in roll.thresholdBands"
        :key="band.name"
        :class="[
          'rounded px-2 py-0.5 text-xs',
          roll.outcomeTier === band.name
            ? 'bg-aircane-600 text-white'
            : 'bg-gray-700 text-gray-400',
        ]"
      >
        {{ band.name }} ({{ band.min }}{{ band.max != null ? `-${band.max}` : '+' }})
      </span>
    </div>
  </div>
</template>

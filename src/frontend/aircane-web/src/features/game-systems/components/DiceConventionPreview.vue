<script setup lang="ts">
import { ref } from 'vue'
import { previewRoll } from '../api'

const props = defineProps<{
  /** The game system definition ID for preview rolls. */
  definitionId?: string
  /** The dice convention JSON for display. */
  conventionJson?: string
}>()

const formula = ref('')
const results = ref<number[] | null>(null)
const total = ref<number | null>(null)
const previewError = ref<string | null>(null)
const rolling = ref(false)

async function handlePreviewRoll(): Promise<void> {
  if (!props.definitionId || !formula.value.trim()) return
  rolling.value = true
  previewError.value = null
  results.value = null
  total.value = null
  try {
    const response = await previewRoll(props.definitionId, formula.value.trim())
    results.value = response.results
    total.value = response.total
  } catch (err) {
    previewError.value = err instanceof Error ? err.message : 'Preview failed.'
  } finally {
    rolling.value = false
  }
}
</script>

<template>
  <div class="space-y-3">
    <h4 class="text-sm font-medium text-gray-200">Dice Convention Preview</h4>

    <!-- Convention display -->
    <div v-if="conventionJson" class="rounded-lg border border-gray-700 bg-gray-900 p-3">
      <pre class="text-xs text-gray-300 overflow-x-auto">{{ conventionJson }}</pre>
    </div>

    <!-- Live roll preview -->
    <div v-if="definitionId" class="space-y-2">
      <div class="flex gap-2">
        <input
          v-model="formula"
          type="text"
          placeholder="e.g. 1d20+5"
          class="flex-1 rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          @keydown.enter="handlePreviewRoll"
        />
        <button
          type="button"
          :disabled="rolling || !formula.trim()"
          class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50"
          @click="handlePreviewRoll"
        >
          Roll
        </button>
      </div>

      <!-- Results -->
      <div v-if="results" class="rounded-lg border border-gray-700 bg-gray-800 p-3">
        <p class="text-xs text-gray-400">Results: {{ results.join(', ') }}</p>
        <p class="text-lg font-bold text-aircane-400">Total: {{ total }}</p>
      </div>

      <p v-if="previewError" class="text-xs text-red-400">{{ previewError }}</p>
    </div>
  </div>
</template>

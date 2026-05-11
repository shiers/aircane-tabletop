<script setup lang="ts">
import { computed } from 'vue'
import { ImportStatus } from '../api'

const props = defineProps<{
  status: ImportStatus
}>()

interface BadgeConfig {
  label: string
  classes: string
  spinning: boolean
}

const config = computed<BadgeConfig>(() => {
  switch (props.status) {
    case ImportStatus.Pending:
      return { label: 'Pending', classes: 'bg-gray-700 text-gray-300', spinning: false }
    case ImportStatus.Processing:
      return { label: 'Processing', classes: 'bg-blue-900 text-blue-300', spinning: true }
    case ImportStatus.Completed:
      return { label: 'Completed', classes: 'bg-green-900 text-green-300', spinning: false }
    case ImportStatus.Failed:
      return { label: 'Failed', classes: 'bg-red-900 text-red-300', spinning: false }
    case ImportStatus.OcrRequired:
      return { label: 'OCR Required', classes: 'bg-yellow-900 text-yellow-300', spinning: false }
    default:
      return { label: 'Unknown', classes: 'bg-gray-700 text-gray-400', spinning: false }
  }
})
</script>

<template>
  <span
    :class="[
      'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-medium',
      config.classes,
    ]"
    :aria-label="`Import status: ${config.label}`"
  >
    <!-- Spinning indicator for Processing state -->
    <svg
      v-if="config.spinning"
      class="h-3 w-3 animate-spin"
      xmlns="http://www.w3.org/2000/svg"
      fill="none"
      viewBox="0 0 24 24"
      aria-hidden="true"
    >
      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
      <path
        class="opacity-75"
        fill="currentColor"
        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
      />
    </svg>
    {{ config.label }}
  </span>
</template>

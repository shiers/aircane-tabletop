<script setup lang="ts">
import { computed } from 'vue'

/**
 * Presentational document-status badge for the library view.
 *
 * Asset note: only `parsed` and `processing` have generated PNGs
 * (`/assets/library/document-status-parsed-badge.png` and
 * `document-status-processing-badge.png`). No PNG exists for the `error` or
 * `ocr-required` states, so those are rendered purely with CSS (red / amber)
 * using the design tokens. parsed/processing render the PNG badge behind a
 * matching CSS fallback so the label stays legible if the image fails to load.
 */
const props = defineProps<{
  status: 'parsed' | 'processing' | 'error' | 'ocr-required'
}>()

interface BadgeConfig {
  label: string
  /** Absolute path to the badge art, or null when the state is CSS-only. */
  image: string | null
  spinning: boolean
}

const config = computed<BadgeConfig>(() => {
  switch (props.status) {
    case 'parsed':
      return {
        label: 'Parsed',
        image: '/assets/library/document-status-parsed-badge.png',
        spinning: false,
      }
    case 'processing':
      return {
        label: 'Processing',
        image: '/assets/library/document-status-processing-badge.png',
        spinning: true,
      }
    case 'error':
      // CSS-only (no PNG asset): red.
      return { label: 'Error', image: null, spinning: false }
    case 'ocr-required':
      // CSS-only (no PNG asset): amber.
      return { label: 'OCR Required', image: null, spinning: false }
    default:
      return { label: 'Unknown', image: null, spinning: false }
  }
})
</script>

<template>
  <span
    class="document-status-badge"
    :class="status"
    :style="config.image ? { backgroundImage: `url('${config.image}')` } : undefined"
    :aria-label="`Document status: ${config.label}`"
  >
    <!-- Spinning indicator for the processing state -->
    <svg
      v-if="config.spinning"
      class="spinner"
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
    <span>{{ config.label }}</span>
  </span>
</template>

<style scoped>
.document-status-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 12px;
  border-radius: var(--border-radius-pill);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.5;
  /* parsed/processing paint their PNG behind the text; the background-color
     is both the CSS fallback and the sole visual for the PNG-less states. */
  background-size: 100% 100%;
  background-repeat: no-repeat;
  background-position: center;
}

.document-status-badge.parsed {
  color: var(--color-green);
  background-color: #0d1a0d;
}

.document-status-badge.processing {
  color: var(--color-blue);
  background-color: #0d1420;
}

.document-status-badge.error {
  color: var(--color-red);
  background-color: #1a0d0d;
  border: 1px solid var(--color-red);
  box-shadow: var(--glow-red);
}

.document-status-badge.ocr-required {
  color: var(--color-amber);
  background-color: #1a140d;
  border: 1px solid var(--color-amber);
}

.spinner {
  width: 12px;
  height: 12px;
  animation: document-status-spin 1s linear infinite;
}

@keyframes document-status-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>

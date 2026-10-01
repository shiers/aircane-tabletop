<script setup lang="ts">
import { ref, watch } from 'vue'
import { listLicenses, type LicenseInfo } from '../licenses'

const props = defineProps<{
  /** Controls modal visibility. */
  open: boolean
  /** Optional documentId to scroll to / highlight when opened. */
  focusDocumentId?: string | null
}>()

const emit = defineEmits<{ close: [] }>()

const licenses = ref<LicenseInfo[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

/** Tailwind classes for each license badge. Falls back to gray for unknown keys. */
function badgeClasses(licenseKey: string | null): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300 ring-1 ring-inset ring-teal-700/50'
    case 'orc':
      return 'bg-purple-900 text-purple-300 ring-1 ring-inset ring-purple-700/50'
    default:
      return 'bg-gray-800 text-gray-300 ring-1 ring-inset ring-gray-600/50'
  }
}

function badgeLabel(license: LicenseInfo): string {
  return license.licenseDisplayName ?? license.licenseKey ?? 'Unknown license'
}

async function loadLicenses(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    licenses.value = await listLicenses()
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load license information.'
  } finally {
    loading.value = false
  }
}

function scrollToFocused(): void {
  const id = props.focusDocumentId
  if (!id) return
  // Wait for the list to render, then scroll the target entry into view.
  requestAnimationFrame(() => {
    const el = document.getElementById(`license-entry-${id}`)
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  })
}

// Load licenses whenever the modal opens; scroll to the focused entry once loaded.
watch(
  () => props.open,
  async (isOpen) => {
    if (isOpen) {
      if (licenses.value.length === 0) await loadLicenses()
      scrollToFocused()
    }
  },
)

watch(
  () => props.focusDocumentId,
  () => {
    if (props.open) scrollToFocused()
  },
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="license-modal-title"
      @click.self="emit('close')"
    >
      <div
        class="flex max-h-[85vh] w-full max-w-2xl flex-col overflow-hidden rounded-xl border border-surface-700 bg-surface-900 shadow-2xl"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b border-surface-700 px-5 py-4">
          <h2 id="license-modal-title" class="text-lg font-semibold text-white">
            Open Content Licenses
          </h2>
          <button
            class="rounded p-1 text-gray-400 hover:bg-surface-800 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            aria-label="Close"
            @click="emit('close')"
          >
            <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-5 py-4">
          <p class="mb-4 text-sm text-gray-400">
            Aircane's built-in rules content is distributed under open-content licenses. The
            attributions below are required by those licenses.
          </p>

          <div v-if="loading" class="py-8 text-center text-sm text-gray-500" aria-busy="true">
            Loading license information…
          </div>

          <div
            v-else-if="error"
            class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
            role="alert"
          >
            {{ error }}
          </div>

          <div v-else-if="licenses.length === 0" class="py-8 text-center text-sm text-gray-500">
            No built-in content is currently installed.
          </div>

          <ul v-else class="space-y-4">
            <li
              v-for="doc in licenses"
              :id="`license-entry-${doc.documentId}`"
              :key="doc.documentId"
              class="rounded-lg border border-surface-700/60 bg-surface-850 p-4"
              :class="focusDocumentId === doc.documentId ? 'ring-2 ring-aircane-500' : ''"
            >
              <div class="flex flex-wrap items-center gap-2">
                <h3 class="font-medium text-white">{{ doc.documentTitle }}</h3>
                <span
                  class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
                  :class="badgeClasses(doc.licenseKey)"
                >
                  {{ badgeLabel(doc) }}
                </span>
              </div>

              <p v-if="doc.attributionText" class="mt-2 text-sm leading-relaxed text-gray-400">
                {{ doc.attributionText }}
              </p>

              <div class="mt-2 flex flex-wrap gap-4 text-sm">
                <a
                  v-if="doc.attributionUrl"
                  :href="doc.attributionUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="text-aircane-400 hover:text-aircane-300 hover:underline"
                >
                  Source ↗
                </a>
                <a
                  v-if="doc.licenseUrl"
                  :href="doc.licenseUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="text-aircane-400 hover:text-aircane-300 hover:underline"
                >
                  License text ↗
                </a>
              </div>
            </li>
          </ul>
        </div>

        <!-- Footer -->
        <div class="border-t border-surface-700 px-5 py-3 text-right">
          <button
            class="rounded-md bg-surface-800 px-4 py-2 text-sm font-medium text-gray-200 hover:bg-surface-700 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            @click="emit('close')"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

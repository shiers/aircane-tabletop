<script setup lang="ts">
import { ref, watch } from 'vue'
import { listLicenses, getOglText, getSection15, type LicenseInfo } from '../licenses'

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

/** Per-document expand state and lazily-loaded OGL/Section-15 text. */
const oglExpanded = ref<Record<string, boolean>>({})
const section15Expanded = ref<Record<string, boolean>>({})
const oglText = ref<Record<string, string>>({})
const section15Text = ref<Record<string, string>>({})
const textLoading = ref<Record<string, boolean>>({})

/** Tailwind classes for each license badge. Falls back to gray for unknown keys. */
function badgeClasses(licenseKey: string | null): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300 ring-1 ring-inset ring-teal-700/50'
    case 'orc':
      return 'bg-purple-900 text-purple-300 ring-1 ring-inset ring-purple-700/50'
    case 'ogl-1.0a':
      return 'bg-amber-900 text-amber-300 ring-1 ring-inset ring-amber-700/50'
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

async function toggleOgl(doc: LicenseInfo): Promise<void> {
  const id = doc.documentId
  const next = !oglExpanded.value[id]
  oglExpanded.value = { ...oglExpanded.value, [id]: next }
  if (next && oglText.value[id] === undefined) {
    textLoading.value = { ...textLoading.value, [`ogl-${id}`]: true }
    try {
      oglText.value = { ...oglText.value, [id]: await getOglText(id) }
    } catch {
      oglText.value = { ...oglText.value, [id]: 'Failed to load OGL license text.' }
    } finally {
      textLoading.value = { ...textLoading.value, [`ogl-${id}`]: false }
    }
  }
}

async function toggleSection15(doc: LicenseInfo): Promise<void> {
  const id = doc.documentId
  const next = !section15Expanded.value[id]
  section15Expanded.value = { ...section15Expanded.value, [id]: next }
  if (next && section15Text.value[id] === undefined) {
    textLoading.value = { ...textLoading.value, [`s15-${id}`]: true }
    try {
      section15Text.value = { ...section15Text.value, [id]: await getSection15(id) }
    } catch {
      section15Text.value = { ...section15Text.value, [id]: 'Failed to load Section 15 text.' }
    } finally {
      textLoading.value = { ...textLoading.value, [`s15-${id}`]: false }
    }
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

              <!-- OGL-only: collapsible full license text + Section 15 -->
              <div v-if="doc.isOgl" class="mt-3 space-y-2">
                <div>
                  <button
                    class="flex items-center gap-1 text-xs font-medium text-gray-300 hover:text-white"
                    :aria-expanded="!!oglExpanded[doc.documentId]"
                    @click="toggleOgl(doc)"
                  >
                    <span>{{ oglExpanded[doc.documentId] ? '▾' : '▸' }}</span>
                    Full OGL v1.0a text
                  </button>
                  <pre
                    v-if="oglExpanded[doc.documentId]"
                    class="mt-2 max-h-64 overflow-auto whitespace-pre-wrap rounded border border-surface-700 bg-surface-950 p-3 text-xs text-gray-400"
                  >{{ textLoading[`ogl-${doc.documentId}`] ? 'Loading…' : oglText[doc.documentId] }}</pre>
                </div>

                <div>
                  <button
                    class="flex items-center gap-1 text-xs font-medium text-gray-300 hover:text-white"
                    :aria-expanded="!!section15Expanded[doc.documentId]"
                    @click="toggleSection15(doc)"
                  >
                    <span>{{ section15Expanded[doc.documentId] ? '▾' : '▸' }}</span>
                    Section 15 (attribution chain)
                  </button>
                  <pre
                    v-if="section15Expanded[doc.documentId]"
                    class="mt-2 max-h-64 overflow-auto whitespace-pre-wrap rounded border border-surface-700 bg-surface-950 p-3 text-xs text-gray-400"
                  >{{ textLoading[`s15-${doc.documentId}`] ? 'Loading…' : section15Text[doc.documentId] }}</pre>
                </div>
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

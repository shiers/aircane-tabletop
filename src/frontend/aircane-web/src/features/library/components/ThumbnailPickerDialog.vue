<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import ThumbnailPicker from './ThumbnailPicker.vue'
import { isKnownThumbnailKey } from '../thumbnails'

const props = withDefaults(
  defineProps<{
    open: boolean
    title?: string
    /** Optional line under the title (for example the document's name). */
    subtitle?: string
    /** The stored key (null = automatic). A key with no image is shown as "no longer available". */
    currentKey: string | null
    /** What Automatic would pick right now, or null when it differs per file. */
    autoKey: string | null
    autoCaption?: string
    applyLabel?: string
  }>(),
  { title: 'Choose a thumbnail', subtitle: undefined, autoCaption: undefined, applyLabel: 'Use this thumbnail' },
)

const emit = defineEmits<{ close: []; select: [key: string | null] }>()

const dialog = ref<HTMLElement | null>(null)
const draft = ref<string | null>(null)
const unavailable = ref(false)
let opener: HTMLElement | null = null

watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      unavailable.value = !!props.currentKey && !isKnownThumbnailKey(props.currentKey)
      draft.value = isKnownThumbnailKey(props.currentKey) ? props.currentKey : null
      opener = document.activeElement instanceof HTMLElement ? document.activeElement : null
      void nextTick(() => {
        const checked = dialog.value?.querySelector<HTMLElement>('[role="radio"][aria-checked="true"]')
        ;(checked ?? dialog.value)?.focus()
      })
    } else {
      opener?.focus()
      opener = null
    }
  },
  { immediate: true },
)

function focusable(): HTMLElement[] {
  if (!dialog.value) return []
  return [
    ...dialog.value.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ),
  ].filter((el) => el.tabIndex >= 0)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.stopPropagation()
    emit('close')
    return
  }
  if (event.key !== 'Tab') return
  // Keep focus inside the dialog.
  const items = focusable()
  if (items.length === 0) {
    event.preventDefault()
    return
  }
  const first = items[0]
  const last = items[items.length - 1]
  const active = document.activeElement
  if (event.shiftKey && (active === first || !dialog.value?.contains(active))) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && (active === last || !dialog.value?.contains(active))) {
    event.preventDefault()
    first.focus()
  }
}

function apply(): void {
  emit('select', draft.value)
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-[60] flex items-center justify-center bg-black/70 p-3 sm:p-4"
      @click.self="emit('close')"
    >
      <div
        ref="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="thumbnail-picker-title"
        tabindex="-1"
        class="flex max-h-[92vh] w-full max-w-3xl flex-col overflow-hidden rounded-xl border border-gray-700 bg-gray-900 shadow-2xl"
        @keydown="onKeydown"
      >
        <div class="flex items-start justify-between gap-3 border-b border-gray-700 px-4 py-3 sm:px-5">
          <div class="min-w-0">
            <h2 id="thumbnail-picker-title" class="text-lg font-semibold text-white">{{ title }}</h2>
            <p v-if="subtitle" class="mt-0.5 truncate text-xs text-gray-400">{{ subtitle }}</p>
          </div>
          <button
            type="button"
            class="flex h-11 w-11 shrink-0 items-center justify-center rounded text-gray-400 hover:bg-gray-800 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            aria-label="Close"
            @click="emit('close')"
          >
            <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div class="flex-1 overflow-y-auto px-4 py-4 sm:px-5">
          <ThumbnailPicker
            v-model="draft"
            :auto-key="autoKey"
            :auto-caption="autoCaption"
            :unavailable="unavailable"
          />
        </div>

        <div class="flex items-center justify-end gap-3 border-t border-gray-700 px-4 py-3 sm:px-5">
          <button
            type="button"
            class="min-h-[44px] rounded-lg px-4 text-sm font-medium text-gray-300 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-gray-500"
            @click="emit('close')"
          >
            Cancel
          </button>
          <button
            type="button"
            data-action="apply"
            class="min-h-[44px] rounded-lg bg-aircane-600 px-5 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            @click="apply"
          >
            {{ applyLabel }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

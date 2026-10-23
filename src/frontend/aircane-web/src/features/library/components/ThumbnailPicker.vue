<script setup lang="ts">
import { computed, nextTick, ref } from 'vue'
import DocumentThumbnail from './DocumentThumbnail.vue'
import {
  THUMBNAIL_FILTERS,
  THUMBNAIL_MANIFEST,
  thumbnailLabel,
} from '../thumbnails'

const props = defineProps<{
  /** The selected key, or null for Automatic. */
  modelValue: string | null
  /**
   * The thumbnail Automatic would pick right now. Null when it differs per file (the batch picker in the
   * folder import), in which case the Automatic tile shows an ornament instead.
   */
  autoKey: string | null
  /** Text under the Automatic tile. Defaults to the label of `autoKey`. */
  autoCaption?: string
  /** True when the stored key no longer has an image; Automatic is then shown as selected. */
  unavailable?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [key: string | null] }>()

const activeFilter = ref('all')
const group = ref<HTMLElement | null>(null)

const visibleEntries = computed(() =>
  activeFilter.value === 'all'
    ? THUMBNAIL_MANIFEST
    : THUMBNAIL_MANIFEST.filter((e) => e.tags.includes(activeFilter.value)),
)

/** Every option in order: Automatic (null) first, then the filtered thumbnails. */
const options = computed<(string | null)[]>(() => [null, ...visibleEntries.value.map((e) => e.key)])

/** The option that is in the tab order: the selected one, or Automatic when the selection is filtered out. */
const tabbable = computed<string | null>(() =>
  options.value.includes(props.modelValue) ? props.modelValue : null,
)

const autoCaptionText = computed(
  () => props.autoCaption ?? (props.autoKey ? thumbnailLabel(props.autoKey) : 'Chosen for each file'),
)

function select(key: string | null): void {
  emit('update:modelValue', key)
}

function focusOption(key: string | null): void {
  void nextTick(() => {
    const el = group.value?.querySelector<HTMLElement>(`[data-option="${key ?? ''}"]`)
    el?.focus()
  })
}

function onKeydown(event: KeyboardEvent): void {
  const list = options.value
  const current = list.indexOf(tabbable.value)
  let next = current
  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowDown':
      next = (current + 1) % list.length
      break
    case 'ArrowLeft':
    case 'ArrowUp':
      next = (current - 1 + list.length) % list.length
      break
    case 'Home':
      next = 0
      break
    case 'End':
      next = list.length - 1
      break
    default:
      return
  }
  event.preventDefault()
  select(list[next])
  focusOption(list[next])
}

function setFilter(id: string): void {
  activeFilter.value = id
}
</script>

<template>
  <div class="min-w-0">
    <p
      v-if="unavailable"
      role="status"
      class="mb-3 rounded-lg border border-amber-700/50 bg-amber-950/30 px-3 py-2 text-sm text-amber-300"
    >
      This thumbnail is no longer available. Automatic is selected.
    </p>

    <!-- Filter chips -->
    <div role="group" aria-label="Filter thumbnails" class="mb-3 flex flex-wrap gap-2">
      <button
        v-for="f in THUMBNAIL_FILTERS"
        :key="f.id"
        type="button"
        :aria-pressed="activeFilter === f.id"
        :data-filter="f.id"
        class="min-h-[44px] rounded-full border px-4 text-sm font-medium focus:outline-none focus:ring-2 focus:ring-aircane-500"
        :class="
          activeFilter === f.id
            ? 'border-aircane-500 bg-aircane-900 text-aircane-200'
            : 'border-gray-700 bg-gray-800 text-gray-300 hover:bg-gray-700'
        "
        @click="setFilter(f.id)"
      >
        {{ f.label }}
      </button>
    </div>

    <!-- Options (radio group; arrow keys move the selection) -->
    <div
      ref="group"
      role="radiogroup"
      aria-label="Thumbnail"
      class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4"
      @keydown="onKeydown"
    >
      <!-- Automatic -->
      <button
        type="button"
        role="radio"
        data-option=""
        aria-label="Automatic"
        :aria-checked="modelValue === null"
        :tabindex="tabbable === null ? 0 : -1"
        class="flex min-h-[44px] min-w-0 flex-col items-center gap-1.5 rounded-lg border-2 p-2 text-center focus:outline-none focus:ring-2 focus:ring-aircane-400"
        :class="modelValue === null ? 'border-aircane-500 bg-aircane-900/40' : 'border-gray-700 bg-gray-800/60 hover:border-gray-500'"
        @click="select(null)"
      >
        <DocumentThumbnail
          v-if="autoKey"
          :thumb-key="autoKey"
          :alt="`Automatic: ${thumbnailLabel(autoKey)}`"
          :label="thumbnailLabel(autoKey)"
          lazy
          class="aspect-[3/4] w-full"
        />
        <div
          v-else
          class="flex aspect-[3/4] w-full items-center justify-center rounded border border-gray-700 bg-gray-900 text-2xl text-amber-400"
          aria-hidden="true"
        >
          ✦
        </div>
        <span class="text-sm font-medium text-gray-100">Automatic</span>
        <span class="w-full truncate text-xs text-gray-400">{{ autoCaptionText }}</span>
      </button>

      <!-- Thumbnails -->
      <button
        v-for="entry in visibleEntries"
        :key="entry.key"
        type="button"
        role="radio"
        :data-option="entry.key"
        :aria-label="entry.label"
        :aria-checked="modelValue === entry.key"
        :tabindex="tabbable === entry.key ? 0 : -1"
        class="flex min-h-[44px] min-w-0 flex-col items-center gap-1.5 rounded-lg border-2 p-2 text-center focus:outline-none focus:ring-2 focus:ring-aircane-400"
        :class="modelValue === entry.key ? 'border-aircane-500 bg-aircane-900/40' : 'border-gray-700 bg-gray-800/60 hover:border-gray-500'"
        @click="select(entry.key)"
      >
        <DocumentThumbnail
          :thumb-key="entry.key"
          :alt="entry.label"
          :label="entry.label"
          lazy
          class="aspect-[3/4] w-full"
        />
        <span class="w-full truncate text-sm text-gray-100">{{ entry.label }}</span>
      </button>
    </div>
  </div>
</template>

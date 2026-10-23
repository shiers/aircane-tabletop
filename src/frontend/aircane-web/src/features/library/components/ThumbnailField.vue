<script setup lang="ts">
import { computed, ref } from 'vue'
import DocumentThumbnail from './DocumentThumbnail.vue'
import ThumbnailPickerDialog from './ThumbnailPickerDialog.vue'
import { isKnownThumbnailKey, thumbnailLabel } from '../thumbnails'

const props = withDefaults(
  defineProps<{
    /** The chosen key, or null for Automatic. */
    modelValue: string | null
    /** What Automatic gives right now, or null when it differs per file. */
    autoKey: string | null
    /** Name of what the thumbnail is for, used in labels and the dialog subtitle. */
    subject?: string
    /** Text under the Automatic tile in the dialog. */
    autoCaption?: string
    /** Only the thumbnail button (for a table row or list item). */
    compact?: boolean
    /** Heading of the dialog. */
    dialogTitle?: string
  }>(),
  { subject: undefined, autoCaption: undefined, compact: false, dialogTitle: 'Choose a thumbnail' },
)

const emit = defineEmits<{ 'update:modelValue': [key: string | null] }>()

const open = ref(false)

/** The thumbnail that will be used: the chosen one if its image exists, otherwise Automatic's. */
const shownKey = computed<string | null>(() =>
  isKnownThumbnailKey(props.modelValue) ? props.modelValue : props.autoKey,
)

const caption = computed(() => {
  if (props.modelValue && isKnownThumbnailKey(props.modelValue)) return thumbnailLabel(props.modelValue)
  if (props.autoKey) return `Automatic (${thumbnailLabel(props.autoKey)})`
  return 'Automatic (chosen for each file)'
})

const buttonLabel = computed(() =>
  props.subject ? `Change thumbnail for ${props.subject}` : 'Change thumbnail',
)
</script>

<template>
  <div :class="compact ? 'inline-flex' : 'flex items-center gap-3'">
    <button
      type="button"
      data-action="open-thumbnail-picker"
      class="flex shrink-0 items-center justify-center rounded focus:outline-none focus:ring-2 focus:ring-aircane-500"
      :class="compact ? 'h-[53px] w-11' : 'min-h-[44px] min-w-[44px] p-0.5'"
      :aria-label="buttonLabel"
      :title="caption"
      @click="open = true"
    >
      <DocumentThumbnail
        v-if="shownKey"
        :thumb-key="shownKey"
        :label="subject"
        :class="compact ? 'h-[53px] w-10' : 'h-[67px] w-[50px]'"
      />
      <span
        v-else
        class="flex items-center justify-center rounded border border-gray-700 bg-gray-900 text-xl text-amber-400"
        :class="compact ? 'h-[53px] w-10' : 'h-[67px] w-[50px]'"
        aria-hidden="true"
      >
        ✦
      </span>
    </button>

    <div v-if="!compact" class="min-w-0">
      <p class="truncate text-sm text-gray-200" data-test="thumbnail-caption">{{ caption }}</p>
      <div class="mt-1 flex flex-wrap items-center gap-x-3">
        <button
          type="button"
          class="min-h-[44px] rounded px-1 text-sm font-medium text-aircane-300 hover:text-aircane-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          @click="open = true"
        >
          Choose…
        </button>
        <button
          v-if="modelValue"
          type="button"
          data-action="reset-thumbnail"
          class="min-h-[44px] rounded px-1 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          @click="emit('update:modelValue', null)"
        >
          Reset to automatic
        </button>
      </div>
    </div>

    <ThumbnailPickerDialog
      :open="open"
      :title="dialogTitle"
      :subtitle="subject"
      :current-key="modelValue"
      :auto-key="autoKey"
      :auto-caption="autoCaption"
      @close="open = false"
      @select="(key) => emit('update:modelValue', key)"
    />
  </div>
</template>

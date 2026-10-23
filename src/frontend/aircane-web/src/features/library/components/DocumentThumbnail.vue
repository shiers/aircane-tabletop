<script setup lang="ts">
import AircaneImg from '@/shared/components/AircaneImg.vue'
import ThumbnailPlaceholder from '@/shared/components/ThumbnailPlaceholder.vue'
import { thumbnailUrl } from '../thumbnails'

withDefaults(
  defineProps<{
    /** Thumbnail key (an unknown key shows the generic image). */
    thumbKey: string
    /** Alt text. Empty means decorative. */
    alt?: string
    /** Initials shown if the image fails to load. */
    label?: string
    lazy?: boolean
  }>(),
  { alt: '', label: undefined, lazy: false },
)
</script>

<template>
  <!--
    The size comes from the caller's class (use a fixed width and height, or w-full with aspect-[3/4]).
    AircaneImg's own scoped `height: 100%` beats sizing classes on its <img>, so the image is drawn
    inside this fixed-size wrapper and AircaneImg is made to fill it.
  -->
  <div class="relative overflow-hidden rounded [&>.aircane-img]:!absolute [&>.aircane-img]:inset-0">
    <AircaneImg
      :key="thumbKey"
      :src="thumbnailUrl(thumbKey)"
      :alt="alt"
      type="thumbnail"
      :aria-hidden="alt ? undefined : 'true'"
      :loading="lazy ? 'lazy' : undefined"
      draggable="false"
    >
      <template #fallback>
        <ThumbnailPlaceholder :label="label" />
      </template>
    </AircaneImg>
  </div>
</template>

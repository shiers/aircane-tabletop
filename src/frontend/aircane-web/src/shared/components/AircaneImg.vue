<script setup lang="ts">
import { useAsset, FALLBACK_COLORS, type AssetType } from '@/composables/useAsset'

const props = defineProps<{
  src: string
  alt?: string
  type?: AssetType
  fallbackEmoji?: string
  fallbackText?: string
  fallbackColor?: string
}>()

// Let v-bind="$attrs" land on the <img>, not the wrapper.
defineOptions({ inheritAttrs: false })

const { failed, onError } = useAsset({
  path: props.src,
  type: props.type ?? 'thumbnail',
  fallbackColor: props.fallbackColor,
})
</script>

<template>
  <div
    class="aircane-img"
    :class="{ failed }"
    :style="{ backgroundColor: fallbackColor ?? FALLBACK_COLORS[type ?? 'thumbnail'] }"
  >
    <img
      v-if="!failed && src"
      :src="src"
      :alt="alt ?? ''"
      @error="onError"
      v-bind="$attrs"
    />
    <slot v-else name="fallback">
      <span v-if="fallbackEmoji" class="fallback-emoji" aria-hidden="true">{{ fallbackEmoji }}</span>
      <span v-else-if="fallbackText" class="fallback-text">{{ fallbackText }}</span>
    </slot>
  </div>
</template>

<style scoped>
.aircane-img {
  position: relative;
  overflow: hidden;
}
img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.failed {
  border: var(--border-gold-dim);
}
.fallback-emoji {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2em;
  opacity: 0.4;
}
.fallback-text {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  color: var(--color-gold-dim);
  opacity: 0.6;
}
</style>

<script setup lang="ts">
import { ref } from 'vue'

const props = defineProps<{
  icon: string
  label: string
}>()

// Last-resort emoji fallbacks for the real icon-*.png nav items.
const ICON_FALLBACKS: Record<string, string> = {
  'icon-library': '📚',
  'icon-campaign': '🔖',
  'icon-character': '👤',
  'icon-rules': '📖',
  'icon-ai': '✦',
  'icon-adventure': '🌿',
  'icon-dice': '🎲',
  'icon-about': 'ℹ',
  'icon-settings': '⚙',
}

const failed = ref(false)

function onError(): void {
  failed.value = true
  console.warn(`[Aircane] Asset failed to load: /assets/ui/${props.icon}.png`)
}
</script>

<template>
  <span class="nav-icon">
    <img
      v-if="!failed"
      :src="`/assets/ui/${icon}.png`"
      :alt="label"
      @error="onError"
    />
    <span v-else class="fallback" aria-hidden="true">{{ ICON_FALLBACKS[icon] ?? '•' }}</span>
  </span>
</template>

<style scoped>
.nav-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
}
.nav-icon img {
  width: 24px;
  height: 24px;
  object-fit: contain;
}
.fallback {
  font-size: 16px;
  line-height: 1;
}
</style>

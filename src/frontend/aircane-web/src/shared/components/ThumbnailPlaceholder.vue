<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  label?: string
}>()

// First characters of the first two words of the label, upper-cased.
// With no usable label we show only the ✦ ornament.
const initials = computed<string>(() => {
  const words = (props.label ?? '').trim().split(/\s+/).filter(Boolean)
  return words.length ? words.slice(0, 2).map((w) => w[0]!.toUpperCase()).join('') : '✦'
})
</script>

<template>
  <div class="thumbnail-placeholder">
    <span class="ornament" aria-hidden="true">✦</span>
    <span v-if="initials !== '✦'" class="initials">{{ initials }}</span>
  </div>
</template>

<style scoped>
.thumbnail-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  width: 100%;
  height: 100%;
  background: var(--fallback-thumbnail);
  border: var(--border-gold-dim);
  border-radius: var(--border-radius-sm);
}
.ornament {
  font-size: 1.5em;
  color: var(--color-gold);
  line-height: 1;
}
.initials {
  font-size: 13px;
  font-weight: 600;
  color: var(--color-gold-dim);
  letter-spacing: 0.05em;
}
</style>

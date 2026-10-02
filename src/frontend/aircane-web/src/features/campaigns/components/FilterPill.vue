<script setup lang="ts">
withDefaults(
  defineProps<{
    label: string
    count?: number
    active?: boolean
  }>(),
  {
    count: undefined,
    active: false,
  },
)

const emit = defineEmits<{
  click: []
}>()
</script>

<template>
  <button
    type="button"
    class="filter-pill"
    :class="{ active }"
    :aria-pressed="active"
    @click="emit('click')"
  >
    <span class="icon" aria-hidden="true">⊞</span>
    <span class="label">{{ label }}</span>
    <span v-if="count !== undefined" class="count">{{ count }}</span>
  </button>
</template>

<style scoped>
.filter-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 14px;
  border-radius: var(--border-radius-pill);
  background: var(--color-bg-panel);
  border: var(--border-gold-dim);
  font-size: 13px;
  font-weight: 500;
  color: #d1d5db;
  cursor: pointer;
  transition: box-shadow 0.2s ease, border-color 0.2s ease;
}

.filter-pill:hover {
  border-color: var(--color-gold);
}

.filter-pill.active {
  border: var(--border-purple);
  box-shadow: var(--glow-purple);
  color: #fff;
}

.icon {
  font-size: 13px;
  line-height: 1;
}

.count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 20px;
  padding: 0 6px;
  border-radius: var(--border-radius-pill);
  background: rgba(124, 58, 237, 0.2);
  font-size: 11px;
  font-weight: 600;
}

.filter-pill.active .count {
  background: var(--color-purple);
  color: #fff;
}
</style>

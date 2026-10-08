<script setup lang="ts">
defineProps<{
  status: 'active' | 'paused' | 'ended'
}>()

const labels: Record<'active' | 'paused' | 'ended', string> = {
  active: 'Active',
  paused: 'Paused',
  ended: 'Ended',
}
</script>

<template>
  <span class="status-badge" :class="status">
    <span v-if="status === 'paused'" class="glyph" aria-hidden="true">⏸</span>
    <span v-else class="dot" aria-hidden="true" />
    <span class="label">{{ labels[status] }}</span>
  </span>
</template>

<style scoped>
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: var(--border-radius-pill);
  background: var(--color-bg-panel);
  border: var(--border-gold-dim);
  font-size: 13px;
  font-weight: 500;
  color: #e5e7eb;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #6b7280;
}

.glyph {
  font-size: 11px;
  line-height: 1;
  color: var(--color-amber);
}

.status-badge.active .dot {
  background: var(--color-green);
  box-shadow: var(--glow-green);
}

.status-badge.active .label {
  color: var(--color-green);
}

.status-badge.paused .label {
  color: var(--color-amber);
}

.status-badge.ended .dot {
  background: #6b7280;
}

.status-badge.ended .label {
  color: #9ca3af;
}
</style>

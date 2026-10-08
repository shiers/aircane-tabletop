<script setup lang="ts">
defineProps<{
  status: 'healthy' | 'error'
  label: string
}>()
</script>

<template>
  <div class="health-pill" :class="status">
    <span class="dot" aria-hidden="true" />
    <span>{{ label }}</span>
  </div>
</template>

<style scoped>
.health-pill {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: var(--border-radius-pill);
  background: #0d1a0d;
  border: var(--border-gold-dim);
  font-size: 13px;
  font-weight: 500;
}
.health-pill.healthy .dot {
  background: var(--color-green);
  box-shadow: var(--glow-green);
}
.health-pill.error .dot {
  background: var(--color-red);
  box-shadow: var(--glow-red);
}
.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
</style>

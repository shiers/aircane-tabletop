<script setup lang="ts">
const props = defineProps<{
  /** Player (purple) or NPC (green). */
  role: 'Player' | 'NPC'
}>()

const artUrl = (): string =>
  props.role === 'Player'
    ? "url('/assets/characters/player-badge-art.png')"
    : "url('/assets/characters/npc-badge-art.png')"
</script>

<template>
  <span
    class="role-badge"
    :class="role.toLowerCase()"
    :style="{ backgroundImage: artUrl() }"
  >
    <span class="dot" aria-hidden="true" />
    <span class="label">{{ role }}</span>
  </span>
</template>

<style scoped>
.role-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: var(--border-radius-pill);
  background-size: 100% 100%;
  background-repeat: no-repeat;
  font-size: 13px;
  font-weight: 500;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.7);
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.role-badge.player .dot {
  background: var(--color-purple);
  box-shadow: var(--glow-purple);
}

.role-badge.player .label {
  color: var(--color-purple-glow);
}

.role-badge.npc .dot {
  background: var(--color-green);
  box-shadow: var(--glow-green);
}

.role-badge.npc .label {
  color: var(--color-green);
}
</style>

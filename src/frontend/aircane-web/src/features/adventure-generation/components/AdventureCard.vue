<script setup lang="ts">
/**
 * AdventureCard - one generated adventure: title, status badge and a one-line summary.
 * The actions differ per page, so they are supplied through the default slot.
 */
import { computed } from 'vue'
import type { CampaignAdventure } from '../api'

const props = defineProps<{
  adventure: CampaignAdventure
}>()

const meta = computed(() => {
  const a = props.adventure
  const length = a.length ? a.length.charAt(0).toUpperCase() + a.length.slice(1) : null
  return [a.gameSystem, a.mode, length, a.averageLevel != null ? `Level ${a.averageLevel}` : null]
    .filter(Boolean)
    .join(' · ')
})

const statusClass = computed(
  () =>
    ({
      Approved: 'border-green-700 bg-green-900/50 text-green-300',
      Active: 'border-aircane-700 bg-aircane-900/50 text-aircane-300',
      Rejected: 'border-red-700 bg-red-900/50 text-red-300',
    })[props.adventure.status] ?? 'border-yellow-700 bg-yellow-900/50 text-yellow-300',
)
</script>

<template>
  <li
    class="rounded-lg border border-surface-700/50 bg-surface-850 p-4"
    data-testid="adventure-card"
    :data-status="adventure.status"
  >
    <div class="flex items-start justify-between gap-3">
      <h3 class="min-w-0 truncate font-medium text-gray-100" data-testid="adventure-title">✦ {{ adventure.title }}</h3>
      <span class="shrink-0 rounded-full border px-2 py-0.5 text-xs" :class="statusClass" data-testid="adventure-status">{{
        adventure.status
      }}</span>
    </div>
    <p class="mt-1 text-sm text-gray-400" data-testid="adventure-meta">{{ meta }}</p>
    <p v-if="adventure.activeSession" class="mt-1 text-sm text-aircane-300" data-testid="adventure-progress">
      Scene {{ adventure.activeSession.currentSceneNumber }} of {{ adventure.activeSession.totalScenes }} — In Progress
    </p>

    <div class="mt-3 flex flex-wrap items-start justify-end gap-2">
      <slot />
    </div>
  </li>
</template>

<style>
/* Shared button look for adventure card actions (unscoped so slotted buttons pick it up). */
.adventure-action {
  border-radius: 0.375rem;
  border: 1px solid rgb(55 65 81);
  background: rgb(31 41 55);
  padding: 0.25rem 0.75rem;
  font-size: 0.875rem;
  color: rgb(243 244 246);
}
.adventure-action:hover:not(:disabled) {
  background: rgb(55 65 81);
}
.adventure-action:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
.adventure-action--primary {
  border-color: transparent;
  background: var(--color-purple, #7c3aed);
  font-weight: 600;
}
.adventure-action--danger {
  border-color: rgb(153 27 27);
  background: rgb(69 10 10);
  color: rgb(252 165 165);
}
</style>

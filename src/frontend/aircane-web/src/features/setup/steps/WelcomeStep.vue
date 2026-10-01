<script setup lang="ts">
/**
 * Step 1 — Welcome. Intro + "Get started" and a low-prominence "Skip setup" link.
 */
defineEmits<{
  (e: 'next'): void
  (e: 'skip'): void
}>()
</script>

<template>
  <div class="flex flex-col items-center text-center gap-6">
    <div
      class="flex h-16 w-16 items-center justify-center rounded-2xl bg-gradient-to-br from-aircane-600 to-sky-500 text-3xl"
      aria-hidden="true"
    >
      ⚔
    </div>

    <div class="space-y-2">
      <h1 class="text-2xl font-bold text-white">Aircane Tabletop</h1>
      <p class="text-gray-400">AI-assisted tabletop RPG for any system.</p>
    </div>

    <p class="max-w-sm text-gray-300">
      Let's get you set up. This takes about two minutes.
    </p>

    <div class="flex flex-col items-center gap-3 pt-2">
      <button
        type="button"
        class="rounded-lg bg-aircane-600 px-6 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="$emit('next')"
      >
        Get started →
      </button>

      <button
        type="button"
        class="text-xs text-gray-500 hover:text-gray-300 hover:underline"
        @click="$emit('skip')"
      >
        Skip setup — use later
      </button>
    </div>
  </div>
</template>

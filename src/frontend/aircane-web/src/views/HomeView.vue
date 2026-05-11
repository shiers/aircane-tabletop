<script setup lang="ts">
import HealthStatus from '@/shared/components/HealthStatus.vue'

const features = [
  {
    icon: '📚',
    title: 'Document Library',
    description: 'Import rules, adventures, and homebrew PDFs. Indexed for AI-powered retrieval.',
  },
  {
    icon: '⚔️',
    title: 'Campaign Management',
    description: 'Create and manage campaigns with configurable AI roles and authority levels.',
  },
  {
    icon: '🧙',
    title: 'Character Sheets',
    description: 'Import from PDF or JSON, or build characters manually with full schema support.',
  },
  {
    icon: '🎲',
    title: 'Dice Roller',
    description: 'Full expression parser — d20, NdM, advantage, disadvantage, modifiers.',
  },
  {
    icon: '🤖',
    title: 'AI DM Runtime',
    description: 'AI assistant, co-DM, or full DM with structured proposals and host approval.',
  },
  {
    icon: '🗺️',
    title: 'Adventure Generation',
    description: 'Generate solo or group adventures scaled to your party level and tone.',
  },
]
</script>

<template>
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-5xl items-center justify-between">
        <h1 class="text-xl font-bold tracking-tight text-aircane-400">
          Aircane Tabletop
        </h1>
        <HealthStatus />
      </div>
    </header>

    <!-- Hero -->
    <section class="mx-auto max-w-5xl px-6 py-20 text-center">
      <h2 class="mb-4 text-4xl font-extrabold tracking-tight text-white">
        Your AI-Assisted Tabletop RPG
      </h2>
      <p class="mx-auto max-w-xl text-lg text-gray-400">
        Import rules, run campaigns, roll dice, and let the AI DM guide your adventure —
        all local-first, all under your control.
      </p>

      <div class="mt-10 flex flex-wrap justify-center gap-4">
        <RouterLink
          to="/campaigns"
          class="rounded-lg bg-aircane-600 px-6 py-3 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        >
          Start a Campaign
        </RouterLink>
        <RouterLink
          to="/library"
          class="rounded-lg border border-gray-700 px-6 py-3 text-sm font-semibold text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-gray-500"
        >
          Import Documents
        </RouterLink>
        <RouterLink
          to="/rules-lookup"
          class="rounded-lg border border-gray-700 px-6 py-3 text-sm font-semibold text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-gray-500"
        >
          Rules Lookup
        </RouterLink>
      </div>
    </section>

    <!-- Feature grid -->
    <section class="mx-auto max-w-5xl px-6 pb-20">
      <div class="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
        <div
          v-for="feature in features"
          :key="feature.title"
          class="rounded-xl border border-gray-800 bg-gray-900 p-6"
        >
          <div class="mb-3 text-2xl" aria-hidden="true">{{ feature.icon }}</div>
          <h3 class="mb-1 font-semibold text-white">{{ feature.title }}</h3>
          <p class="text-sm text-gray-400">{{ feature.description }}</p>
        </div>
      </div>
    </section>
  </main>
</template>

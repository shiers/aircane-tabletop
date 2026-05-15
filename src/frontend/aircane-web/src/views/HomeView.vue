<script setup lang="ts">
const features = [
  {
    icon: 'library',
    title: 'Document Library',
    description: 'Import rules, adventures, and homebrew PDFs. Indexed for AI-powered retrieval.',
    to: '/library',
    gradient: 'from-blue-600/20 to-indigo-600/20',
    iconColor: 'text-blue-400',
  },
  {
    icon: 'campaigns',
    title: 'Campaign Management',
    description: 'Create and manage campaigns with configurable AI roles and authority levels.',
    to: '/campaigns',
    gradient: 'from-purple-600/20 to-pink-600/20',
    iconColor: 'text-purple-400',
  },
  {
    icon: 'characters',
    title: 'Character Sheets',
    description: 'Import from PDF or JSON, or build characters manually with full schema support.',
    to: '/characters',
    gradient: 'from-emerald-600/20 to-teal-600/20',
    iconColor: 'text-emerald-400',
  },
  {
    icon: 'dice',
    title: 'Dice Roller',
    description: 'Full expression parser — d20, NdM, advantage, disadvantage, modifiers.',
    to: '/sessions',
    gradient: 'from-amber-600/20 to-orange-600/20',
    iconColor: 'text-amber-400',
  },
  {
    icon: 'ai',
    title: 'AI DM Runtime',
    description: 'AI assistant, co-DM, or full DM with structured proposals and host approval.',
    to: '/settings/ai',
    gradient: 'from-cyan-600/20 to-blue-600/20',
    iconColor: 'text-cyan-400',
  },
  {
    icon: 'adventure',
    title: 'Adventure Generation',
    description: 'Generate solo or group adventures scaled to your party level and tone.',
    to: '/adventures/generate',
    gradient: 'from-rose-600/20 to-red-600/20',
    iconColor: 'text-rose-400',
  },
]
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-8">
    <!-- Welcome header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-bold text-white">Welcome back, Dungeon Master</h1>
        <p class="mt-1 text-sm text-gray-400">Your AI-assisted adventure begins here.</p>
      </div>
      <RouterLink to="/campaigns" class="btn-primary">
        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
        </svg>
        New Campaign
      </RouterLink>
    </div>

    <!-- Feature cards grid -->
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <RouterLink
        v-for="feature in features"
        :key="feature.title"
        :to="feature.to"
        class="group relative overflow-hidden rounded-xl border border-surface-700/50 bg-surface-850 p-5 transition-all duration-200 hover:border-aircane-700/50 hover:shadow-lg hover:shadow-aircane-900/20"
      >
        <!-- Gradient overlay -->
        <div
          class="absolute inset-0 bg-gradient-to-br opacity-0 transition-opacity duration-300 group-hover:opacity-100"
          :class="feature.gradient"
        />

        <!-- Content -->
        <div class="relative">
          <!-- Icon -->
          <div class="mb-4 flex h-10 w-10 items-center justify-center rounded-lg bg-surface-800 ring-1 ring-surface-700/50">
            <svg v-if="feature.icon === 'library'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
            </svg>
            <svg v-else-if="feature.icon === 'campaigns'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <svg v-else-if="feature.icon === 'characters'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
            </svg>
            <svg v-else-if="feature.icon === 'dice'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
            </svg>
            <svg v-else-if="feature.icon === 'ai'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
            </svg>
            <svg v-else-if="feature.icon === 'adventure'" class="h-5 w-5" :class="feature.iconColor" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
            </svg>
          </div>

          <h3 class="mb-1.5 font-semibold text-white">{{ feature.title }}</h3>
          <p class="text-sm leading-relaxed text-gray-400">{{ feature.description }}</p>
        </div>
      </RouterLink>
    </div>

    <!-- Quick actions row -->
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <RouterLink
        to="/campaigns"
        class="flex items-center gap-3 rounded-lg border border-surface-700/50 bg-surface-850 px-4 py-3 text-sm font-medium text-gray-300 transition-all hover:border-aircane-700/50 hover:text-white"
      >
        <span class="flex h-8 w-8 items-center justify-center rounded-md bg-aircane-600/20 text-aircane-400">
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
        </span>
        Start AI DM
      </RouterLink>

      <RouterLink
        to="/library"
        class="flex items-center gap-3 rounded-lg border border-surface-700/50 bg-surface-850 px-4 py-3 text-sm font-medium text-gray-300 transition-all hover:border-aircane-700/50 hover:text-white"
      >
        <span class="flex h-8 w-8 items-center justify-center rounded-md bg-blue-600/20 text-blue-400">
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
          </svg>
        </span>
        Import Documents
      </RouterLink>

      <RouterLink
        to="/characters"
        class="flex items-center gap-3 rounded-lg border border-surface-700/50 bg-surface-850 px-4 py-3 text-sm font-medium text-gray-300 transition-all hover:border-aircane-700/50 hover:text-white"
      >
        <span class="flex h-8 w-8 items-center justify-center rounded-md bg-emerald-600/20 text-emerald-400">
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z" />
          </svg>
        </span>
        Create Character
      </RouterLink>

      <RouterLink
        to="/sessions"
        class="flex items-center gap-3 rounded-lg border border-surface-700/50 bg-surface-850 px-4 py-3 text-sm font-medium text-gray-300 transition-all hover:border-aircane-700/50 hover:text-white"
      >
        <span class="flex h-8 w-8 items-center justify-center rounded-md bg-amber-600/20 text-amber-400">
          <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
          </svg>
        </span>
        Roll Dice
      </RouterLink>
    </div>
  </div>
</template>

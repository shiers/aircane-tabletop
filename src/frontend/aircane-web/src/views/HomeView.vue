<script setup lang="ts">
import AircaneImg from '@/shared/components/AircaneImg.vue'
import ThumbnailPlaceholder from '@/shared/components/ThumbnailPlaceholder.vue'

const features = [
  {
    icon: 'library',
    title: 'Document Library',
    description: 'Import rules, adventures, and homebrew PDFs. Indexed for AI-powered retrieval.',
    to: '/library',
    gradient: 'from-blue-600/20 to-indigo-600/20',
    iconColor: 'text-blue-400',
    thumbnail: '/assets/dashboard/document-library-thumbnail.png',
  },
  {
    icon: 'campaigns',
    title: 'Campaign Management',
    description: 'Create and manage campaigns with configurable AI roles and authority levels.',
    to: '/campaigns',
    gradient: 'from-purple-600/20 to-pink-600/20',
    iconColor: 'text-purple-400',
    thumbnail: '/assets/dashboard/campaign-management-thumbnail.png',
  },
  {
    icon: 'characters',
    title: 'Character Sheets',
    description: 'Import from PDF or JSON, or build characters manually with full schema support.',
    to: '/characters',
    gradient: 'from-emerald-600/20 to-teal-600/20',
    iconColor: 'text-emerald-400',
    thumbnail: '/assets/dashboard/character-sheets-thumbnail.png',
  },
  {
    icon: 'dice',
    title: 'Dice Roller',
    description: 'Full expression parser — d20, NdM, advantage, disadvantage, modifiers.',
    to: '/sessions',
    gradient: 'from-amber-600/20 to-orange-600/20',
    iconColor: 'text-amber-400',
    thumbnail: '/assets/dashboard/dice-roller-thumbnail.png',
  },
  {
    icon: 'ai',
    title: 'AI DM Runtime',
    description: 'AI assistant, co-DM, or full DM with structured proposals and host approval.',
    to: '/settings/ai',
    gradient: 'from-cyan-600/20 to-blue-600/20',
    iconColor: 'text-cyan-400',
    thumbnail: '/assets/dashboard/ai-dm-runtime-thumbnail.png',
  },
  {
    icon: 'adventure',
    title: 'Adventure Generation',
    description: 'Generate solo or group adventures scaled to your party level and tone.',
    to: '/adventures/generate',
    gradient: 'from-rose-600/20 to-red-600/20',
    iconColor: 'text-rose-400',
    thumbnail: '/assets/dashboard/adventure-generation-thumbnail.png',
  },
]

const quickActions = [
  {
    label: 'Start AI DM',
    to: '/campaigns',
    art: '/assets/dashboard/quick-action-start-ai-dm-card.png',
  },
  {
    label: 'Import Documents',
    to: '/library',
    art: '/assets/dashboard/quick-action-import-documents-card.png',
  },
  {
    label: 'Create Character',
    to: '/characters',
    art: '/assets/dashboard/quick-action-create-character-card.png',
  },
  {
    label: 'Roll Dice',
    to: '/sessions',
    art: '/assets/dashboard/quick-action-roll-dice-card.png',
  },
]
</script>

<template>
  <div class="mx-auto max-w-6xl space-y-8">
    <!-- Hero banner -->
    <section
      class="hero"
      v-bg-asset="{ url: '/assets/dashboard/dashboard-hero-background.png', fallback: '#0d0d2a' }"
    >
      <!-- Welcome banner overlaid on the left 55% -->
      <div
        class="hero-welcome"
        aria-hidden="true"
        v-bg-asset="{ url: '/assets/dashboard/dashboard-welcome-banner.png', fallback: '#0d0d2a', position: 'left center' }"
      />
      <!-- Heading over the right portion -->
      <div class="hero-heading">
        <h1 class="text-2xl font-bold text-white sm:text-3xl">Welcome back, Dungeon Master</h1>
        <p class="mt-1 text-sm text-gray-200">Your AI-assisted adventure begins here.</p>
      </div>
    </section>

    <!-- Feature cards grid -->
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <RouterLink
        v-for="feature in features"
        :key="feature.title"
        :to="feature.to"
        class="group relative overflow-hidden rounded-xl border border-surface-700/50 bg-surface-850 transition-all duration-200 hover:border-aircane-700/50 hover:shadow-lg hover:shadow-aircane-900/20"
      >
        <!-- Thumbnail -->
        <AircaneImg
          :src="feature.thumbnail"
          alt=""
          type="thumbnail"
          aria-hidden="true"
          class="feature-thumbnail"
        >
          <template #fallback>
            <ThumbnailPlaceholder :label="feature.title" />
          </template>
        </AircaneImg>

        <!-- Gradient overlay -->
        <div
          class="pointer-events-none absolute inset-0 bg-gradient-to-br opacity-0 transition-opacity duration-300 group-hover:opacity-100"
          :class="feature.gradient"
        />

        <!-- Content -->
        <div class="relative p-5">
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

    <!-- Quick Actions panel -->
    <section class="space-y-4">
      <div
        class="panel-header panel-header--quick-actions"
        v-bg-asset="{ url: '/assets/dashboard/quick-actions-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <h2 class="panel-header-title">Quick Actions</h2>
      </div>

      <!-- Quick action cards -->
      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <RouterLink
          v-for="action in quickActions"
          :key="action.label"
          :to="action.to"
          class="quick-action-card"
          v-bg-asset="{ url: action.art, fallback: '#0d0d24' }"
        >
          <span class="quick-action-label">{{ action.label }}</span>
        </RouterLink>
      </div>
    </section>

    <!-- Recent Campaigns panel -->
    <section class="space-y-4">
      <div
        class="panel-header panel-header--recent-campaigns"
        v-bg-asset="{ url: '/assets/dashboard/recent-campaigns-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <h2 class="panel-header-title">Recent Campaigns</h2>
      </div>

      <div class="flex items-center justify-between rounded-xl border border-surface-700/50 bg-surface-850 p-5">
        <p class="text-sm text-gray-400">Jump back into an existing adventure or start a fresh one.</p>
        <RouterLink
          to="/campaigns"
          class="new-campaign-button"
          v-bg-asset="{ url: '/assets/dashboard/new-campaign-button-art.png', fallback: 'transparent', size: '100% 100%' }"
        >
          <span class="new-campaign-label">+ New Campaign</span>
        </RouterLink>
      </div>
    </section>

    <!-- Footer quote ornament -->
    <footer class="footer-quote">
      <img
        src="/assets/dashboard/footer-quote-ornament.png"
        alt=""
        aria-hidden="true"
        class="footer-quote-ornament"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />
      <p class="footer-quote-text">Great stories aren't just imagined. They're played.</p>
    </footer>
  </div>
</template>

<style scoped>
/* Hero banner */
.hero {
  position: relative;
  width: 100%;
  min-height: 200px;
  overflow: hidden;
  border-radius: var(--border-radius-md);
  background-size: cover;
  background-position: center;
}

/* Welcome banner overlaid on the left 55% */
.hero-welcome {
  position: absolute;
  inset: 0 45% 0 0;
  background-size: cover;
  background-position: left center;
}

/* Heading over the right portion */
.hero-heading {
  position: absolute;
  top: 50%;
  right: 5%;
  left: 58%;
  transform: translateY(-50%);
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

/* Feature card thumbnails */
.feature-thumbnail {
  display: block;
  width: 100%;
  height: 160px;
  object-fit: cover;
  border-radius: 8px 8px 0 0;
}

/* Panel headers */
.panel-header {
  position: relative;
  display: flex;
  align-items: center;
  min-height: 72px;
  padding: 0 1.25rem;
  border-radius: var(--border-radius-md);
  background-size: cover;
  background-position: left center;
  background-repeat: no-repeat;
  /* Panel border so Type C panels stay legible with no art. */
  border: var(--border-gold);
}

/* Heading text overlaid over the right 75% */
.panel-header-title {
  margin-left: 25%;
  font-size: 1.125rem;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

/* Quick action cards (background-size: cover, text over right 70%) */
.quick-action-card {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  min-height: 72px;
  padding: 0 1rem;
  border-radius: var(--border-radius-md);
  /* Card base so it stays identifiable/clickable with no art. */
  border: var(--border-gold-dim);
  background-size: cover;
  background-position: center;
  background-repeat: no-repeat;
  transition: box-shadow 0.2s ease, transform 0.2s ease;
}

.quick-action-card:hover {
  box-shadow: var(--glow-purple);
  transform: translateY(-1px);
}

.quick-action-label {
  width: 70%;
  text-align: center;
  font-size: 0.875rem;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.8);
}

/* New Campaign button — art-independent CSS base so it is always clickable. */
.new-campaign-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 180px;
  min-height: 44px;
  padding: 0 1.25rem;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  background-color: var(--color-purple);
  box-shadow: var(--glow-purple);
  background-repeat: no-repeat;
  transition: box-shadow 0.2s ease;
}

.new-campaign-button:hover {
  box-shadow: var(--glow-purple);
}

.new-campaign-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

/* Footer quote ornament */
.footer-quote {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem 0;
}

.footer-quote-ornament {
  display: block;
  width: 100%;
  max-width: 600px;
}

.footer-quote-text {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  font-style: italic;
  color: var(--color-gold);
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
  text-align: center;
  padding: 0 1rem;
}
</style>

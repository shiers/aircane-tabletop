<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { listLicenses, type LicenseInfo } from '@/features/library/licenses'

const licenses = ref<LicenseInfo[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

/**
 * Static license cards shown at the top of the page. These map the two
 * built-in open-content licenses to their dedicated card art. The live
 * attribution list below is still driven by listLicenses().
 */
const licenseCards = [
  {
    key: 'cc-by-4.0',
    title: 'D&D SRD',
    license: 'Creative Commons Attribution 4.0',
    art: '/assets/about/dnd-srd-license-card.png',
    description:
      'System Reference Document content used under the Creative Commons Attribution 4.0 International license (CC BY 4.0).',
  },
  {
    key: 'orc',
    title: 'Pathfinder 2e Remaster',
    license: 'Open RPG Creative (ORC) License',
    art: '/assets/about/pf2e-orc-license-card.png',
    description:
      'Remaster rules reference content used under the Open RPG Creative (ORC) License.',
  },
]

/** Tailwind classes for a license badge by license key. */
function badgeClasses(licenseKey: string | null): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300 ring-1 ring-inset ring-teal-700/50'
    case 'orc':
      return 'bg-purple-900 text-purple-300 ring-1 ring-inset ring-purple-700/50'
    default:
      return 'bg-gray-800 text-gray-300 ring-1 ring-inset ring-gray-600/50'
  }
}

onMounted(async () => {
  loading.value = true
  error.value = null
  try {
    licenses.value = await listLicenses()
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Failed to load license information.'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="about-page min-h-full">
    <div class="about-sections">
      <!-- Hero banner with the title overlaid bottom-left (same as other views). -->
      <div
        class="app-hero"
        v-bg-asset="{ url: '/assets/about/about-hero-banner.png', fallback: '#0d0d2a' }"
        role="img"
        aria-label="About & Credits"
      >
        <div class="app-hero__overlay">
          <h1 class="app-hero__title">About &amp; Credits</h1>
          <p class="app-hero__subtitle">
            Aircane Tabletop — a local-first, AI-assisted tabletop RPG engine.
          </p>
        </div>
      </div>

      <!-- License cards (static): one per built-in open-content license. -->
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div
          v-for="card in licenseCards"
          :key="card.key"
          class="license-card rounded-lg bg-cover bg-left"
          v-bg-asset="{ url: card.art, fallback: '#0d0d2a', position: 'left center' }"
        >
          <div class="ml-auto w-[75%] min-w-[180px]">
            <h3 class="font-semibold text-white">{{ card.title }}</h3>
            <p class="mt-1 text-xs font-medium text-gray-300">{{ card.license }}</p>
            <p class="mt-2 text-xs leading-relaxed text-gray-400">{{ card.description }}</p>
          </div>
        </div>
      </div>

      <!-- Divider -->
      <img
        src="/assets/about/legal-scroll-ornament.png"
        alt=""
        aria-hidden="true"
        class="mx-auto w-full max-w-[600px]"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />

      <!-- Open content attributions (dark panel) -->
      <section
        class="about-panel rounded-lg bg-cover bg-left"
        v-bg-asset="{ url: '/assets/about/open-content-attributions-panel.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <div class="ml-auto w-[75%] min-w-[260px] space-y-4">
          <h2 class="text-lg font-semibold text-white">Open Content &amp; Attributions</h2>
          <p class="text-sm text-gray-400">
            Aircane ships built-in rules reference content under open-content licenses. The
            attributions below are required by those licenses and are reproduced here to satisfy
            them.
          </p>

          <div v-if="loading" class="py-6 text-center text-sm text-gray-500" aria-busy="true">
            Loading attributions…
          </div>
          <div
            v-else-if="error"
            class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
            role="alert"
          >
            {{ error }}
          </div>
          <p v-else-if="licenses.length === 0" class="text-sm text-gray-500">
            No built-in content is currently installed.
          </p>

          <ul v-else class="space-y-4">
            <li v-for="doc in licenses" :key="doc.documentId">
              <div class="flex flex-wrap items-center gap-2">
                <h3 class="font-medium text-white">{{ doc.documentTitle }}</h3>
                <!-- Attribution badge: badge art backs the license label. -->
                <span
                  class="inline-flex items-center rounded-full bg-cover bg-center px-3 py-0.5 text-xs font-medium"
                  :class="badgeClasses(doc.licenseKey)"
                  v-bg-asset="{ url: '/assets/about/attribution-badge.png', fallback: '#0d0d2a' }"
                >
                  {{ doc.licenseDisplayName ?? doc.licenseKey ?? 'Unknown license' }}
                </span>
              </div>
              <p v-if="doc.attributionText" class="mt-2 text-sm leading-relaxed text-gray-400">
                {{ doc.attributionText }}
              </p>
              <div class="mt-2 flex flex-wrap gap-4 text-sm">
                <a
                  v-if="doc.attributionUrl"
                  :href="doc.attributionUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="text-aircane-400 hover:text-aircane-300 hover:underline"
                >
                  Source ↗
                </a>
                <a
                  v-if="doc.licenseUrl"
                  :href="doc.licenseUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="text-aircane-400 hover:text-aircane-300 hover:underline"
                >
                  License text ↗
                </a>
              </div>
            </li>
          </ul>
        </div>
      </section>

      <!-- Divider -->
      <img
        src="/assets/about/legal-scroll-ornament.png"
        alt=""
        aria-hidden="true"
        class="mx-auto w-full max-w-[600px]"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />

      <!-- ORC downstream declaration (dark panel) -->
      <section
        class="about-panel rounded-lg bg-cover bg-left"
        v-bg-asset="{ url: '/assets/about/content-licensing-declaration-panel.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <div class="ml-auto w-[75%] min-w-[260px] space-y-3 text-sm text-gray-400">
          <h2 class="text-lg font-semibold text-white">Content Licensing Declaration</h2>
          <p>
            For content distributed under the Open RPG Creative (ORC) License, Aircane declares the
            following, as the ORC License requires:
          </p>
          <div>
            <p class="font-medium text-gray-200">Licensed (ORC) Content</p>
            <p class="mt-1">
              The game-mechanics rules text contained in Aircane's built-in ORC content files (the
              embedded rules reference bundles). These are Licensed Material under the ORC License.
            </p>
          </div>
          <div>
            <p class="font-medium text-gray-200">Reserved Material</p>
            <p class="mt-1">
              The Aircane application source code, user-interface design, visual assets, and the
              Aircane name and branding are Reserved Material and are not licensed under the ORC
              License. No Product Identity of any third party is reproduced.
            </p>
          </div>
          <p>
            Content under other licenses (Creative Commons Attribution 4.0) is governed by those
            licenses; each bundle's notice and attribution are shown above and in the Library's Open
            Content Licenses view.
          </p>
        </div>
      </section>

      <!-- Divider -->
      <img
        src="/assets/about/legal-scroll-ornament.png"
        alt=""
        aria-hidden="true"
        class="mx-auto w-full max-w-[600px]"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />

      <!-- Application license (dark panel) -->
      <section
        class="about-panel rounded-lg bg-cover bg-left"
        v-bg-asset="{ url: '/assets/about/application-license-panel.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <div class="ml-auto w-[75%] min-w-[260px] space-y-3">
          <h2 class="text-lg font-semibold text-white">Application License</h2>
          <p class="text-sm text-gray-400">
            The Aircane application software is licensed separately from the open rules content
            above. See the project <code class="text-gray-300">README</code> for the current
            application license.
          </p>
        </div>
      </section>

      <!-- Trademark note -->
      <section class="space-y-3">
        <h2 class="text-lg font-semibold text-white">Trademarks</h2>
        <p class="text-sm text-gray-400">
          Product names, logos, and trade dress referenced by open content remain the property of
          their respective owners and are not claimed by Aircane. Aircane does not use third-party
          fonts, logos, or trade dress in its interface.
        </p>
      </section>
    </div>
  </div>
</template>

<style scoped>
/* Increased breathing room: ≥40px vertical gap between every major section
   (cards, dark panels, scroll ornaments). */
.about-sections {
  display: flex;
  flex-direction: column;
  gap: 40px;
}

/* Type C panels/cards — gold border so they stay bordered/legible with no art. */
.license-card,
.about-panel {
  border: var(--border-gold);
}

/* Parchment license cards: 32px padding on all sides (overrides Tailwind p-5). */
.license-card {
  padding: 32px;
}

/* Dark attribution panels: 28px vertical / 32px horizontal (overrides p-5). */
.about-panel {
  padding: 28px 32px;
}
</style>

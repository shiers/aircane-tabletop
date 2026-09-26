<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { listLicenses, type LicenseInfo } from '@/features/library/licenses'

const licenses = ref<LicenseInfo[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

/** Tailwind classes for a license badge by license key. */
function badgeClasses(licenseKey: string | null): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300 ring-1 ring-inset ring-teal-700/50'
    case 'orc':
      return 'bg-purple-900 text-purple-300 ring-1 ring-inset ring-purple-700/50'
    case 'ogl-1.0a':
      return 'bg-amber-900 text-amber-300 ring-1 ring-inset ring-amber-700/50'
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
  <div class="mx-auto max-w-3xl space-y-8">
    <!-- Header -->
    <div>
      <h1 class="text-2xl font-bold text-white">About &amp; Credits</h1>
      <p class="mt-1 text-sm text-gray-400">
        Aircane Tabletop — a local-first, AI-assisted tabletop RPG engine.
      </p>
    </div>

    <!-- Open content attributions -->
    <section class="space-y-4">
      <h2 class="text-lg font-semibold text-white">Open Content &amp; Attributions</h2>
      <p class="text-sm text-gray-400">
        Aircane ships built-in rules reference content under open-content licenses. The
        attributions below are required by those licenses and are reproduced here to satisfy them.
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
        <li
          v-for="doc in licenses"
          :key="doc.documentId"
          class="rounded-lg border border-surface-700/60 bg-surface-850 p-4"
        >
          <div class="flex flex-wrap items-center gap-2">
            <h3 class="font-medium text-white">{{ doc.documentTitle }}</h3>
            <span
              class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium"
              :class="badgeClasses(doc.licenseKey)"
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
            <RouterLink
              v-if="doc.isOgl"
              to="/library"
              class="text-aircane-400 hover:text-aircane-300 hover:underline"
            >
              Full OGL text &amp; Section 15 (in Library)
            </RouterLink>
          </div>
        </li>
      </ul>
    </section>

    <!-- ORC downstream declaration -->
    <section class="space-y-3">
      <h2 class="text-lg font-semibold text-white">Content Licensing Declaration</h2>
      <div class="rounded-lg border border-surface-700/60 bg-surface-850 p-4 text-sm text-gray-400 space-y-3">
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
          Content under other licenses (Creative Commons Attribution 4.0, Open Game License v1.0a)
          is governed by those licenses; each bundle's notice and attribution are shown above and
          in the Library's Open Content Licenses view.
        </p>
      </div>
    </section>

    <!-- Application license -->
    <section class="space-y-3">
      <h2 class="text-lg font-semibold text-white">Application License</h2>
      <p class="text-sm text-gray-400">
        The Aircane application software is licensed separately from the open rules content above.
        See the project <code class="text-gray-300">README</code> for the current application
        license.
      </p>
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
</template>

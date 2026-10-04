<script setup lang="ts">
/**
 * JoinSessionView - the page players open when they follow a join URL.
 * Route: /join/:sessionId
 *
 * Loads the session details, then shows the PlayerJoinForm.
 * On successful join, shows a waiting/confirmation state.
 */
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useSessionStore } from './store'
import PlayerJoinForm from './PlayerJoinForm.vue'
import QRCode from './components/QRCode.vue'
import type { JoinSessionResult } from './api'

// ---------------------------------------------------------------------------
// Route params
// ---------------------------------------------------------------------------

const route = useRoute()
const sessionId = (route.params.sessionId as string | undefined) ?? ''

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const store = useSessionStore()
const joinResult = ref<JoinSessionResult | null>(null)

/**
 * Dedicated lookup state, tracked locally and independent of the shared store.
 * This keeps a failed session lookup from being confused with a join-submit
 * error (store.error) or with stale session state the store may still hold.
 */
const lookupLoading = ref(false)
const lookupError = ref<string | null>(null)

/** The current page URL so players can hand off the join link via QR. */
const joinUrl = computed(() =>
  typeof window !== 'undefined' ? window.location.href : '',
)

// ---------------------------------------------------------------------------
// Load session info
// ---------------------------------------------------------------------------

onMounted(async () => {
  // No pre-populated session ID (e.g. the /sessions index): skip the lookup so
  // the join form always renders instead of hanging on an invalid request.
  if (!sessionId) return
  lookupLoading.value = true
  try {
    await store.fetchSession(sessionId)
  } catch {
    // Record the failure in dedicated lookup state so the standalone
    // "Session not found" panel owns it (store.error is left for join submits).
    lookupError.value = store.error ?? 'Session not found.'
  } finally {
    lookupLoading.value = false
  }
})

// ---------------------------------------------------------------------------
// Join handler
// ---------------------------------------------------------------------------

function handleJoined(result: JoinSessionResult): void {
  joinResult.value = result
}
</script>

<template>
  <div class="join-page min-h-full">
    <div class="page-sections mx-auto max-w-lg">
      <!-- Hero banner with the title overlaid bottom-left -->
      <div
        class="app-hero"
        v-bg-asset="{ url: '/assets/join-session/join-session-hero-banner.png', fallback: '#0d0d2a' }"
      >
        <div class="app-hero__overlay">
          <h1 class="app-hero__title">Join Session</h1>
          <p class="app-hero__subtitle">
            Enter your display name and invite code to join the table.
          </p>
        </div>
      </div>

      <!-- Loading state (only while an ID-backed lookup is in flight) -->
      <div
        v-if="lookupLoading"
        class="flex items-center justify-center py-20"
        aria-live="polite"
        aria-busy="true"
      >
        <span class="text-gray-400">Loading session…</span>
      </div>

      <!-- Joined - waiting for approval -->
      <div
        v-else-if="joinResult"
        class="rounded-xl border border-surface-700/50 bg-surface-850 p-8 text-center"
        aria-live="polite"
      >
        <div class="mb-4 text-4xl" aria-hidden="true">🎲</div>
        <h2 class="mb-2 text-xl font-semibold text-white">
          Welcome, {{ joinResult.displayName }}!
        </h2>
        <p v-if="joinResult.isApproved" class="text-sm text-green-400">
          You have joined the session. The host will set up your character shortly.
        </p>
        <p v-else class="text-sm text-yellow-400">
          Your join request is pending host approval. Please wait…
        </p>
        <p class="mt-4 text-xs text-gray-500">
          Keep this page open to stay connected.
        </p>
      </div>

      <!-- Join form + QR hand-off. The form always renders first (regardless of
           whether the session loaded), with the error panel shown BELOW it. -->
      <template v-else>
        <PlayerJoinForm
          :session-id="sessionId"
          :session-name="lookupError ? undefined : store.currentSession?.name"
          :hide-error="!!lookupError"
          @joined="handleJoined"
        />

        <!-- Join by QR card: QR over the left 35%, heading/subtext over the right 65% -->
        <div
          class="qr-card flex items-center gap-4 rounded-xl p-5"
          v-bg-asset="{ url: '/assets/join-session/join-by-qr-card.png', fallback: '#0d0d2a', position: 'left center' }"
        >
          <div class="flex w-[35%] shrink-0 justify-center">
            <QRCode :value="joinUrl" :size="140" />
          </div>
          <div class="w-[65%]">
            <h2 class="mb-1 text-lg font-semibold text-white">Join by QR Code</h2>
            <p class="text-sm text-gray-300">
              Scan this code with another device to open this join page there.
            </p>
          </div>
        </div>

        <!-- Session-not-found panel: only when the ID-backed lookup failed.
             PlayerJoinForm surfaces join (API) errors itself, so this standalone
             panel is scoped to the load failure (lookupError) to avoid a
             duplicate error being shown. -->
        <div
          v-if="lookupError"
          role="alert"
          class="not-found-panel flex min-h-[14rem] flex-col items-center justify-center rounded-xl px-6 py-8 text-center"
          v-bg-asset="{ url: '/assets/join-session/session-not-found-error-panel.png', fallback: '#0d0d2a', position: 'left center' }"
        >
          <p class="mb-2 text-lg font-semibold text-red-300">Session not found</p>
          <p class="text-sm text-red-400">{{ lookupError }}</p>
          <RouterLink
            to="/"
            class="mt-4 inline-block text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          >
            ← Back to home
          </RouterLink>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.not-found-panel {
  border: var(--border-gold);
  background-size: cover;
  background-position: left center;
}

.qr-card {
  border: var(--border-gold);
  background-size: cover;
  background-position: left center;
}
</style>

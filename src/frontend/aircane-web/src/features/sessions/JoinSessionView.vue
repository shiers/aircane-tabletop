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
const sessionId = route.params.sessionId as string

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const store = useSessionStore()
const joinResult = ref<JoinSessionResult | null>(null)

/** The current page URL so players can hand off the join link via QR. */
const joinUrl = computed(() =>
  typeof window !== 'undefined' ? window.location.href : '',
)

// ---------------------------------------------------------------------------
// Load session info
// ---------------------------------------------------------------------------

onMounted(async () => {
  await store.fetchSession(sessionId)
})

// ---------------------------------------------------------------------------
// Join handler
// ---------------------------------------------------------------------------

function handleJoined(result: JoinSessionResult): void {
  joinResult.value = result
}
</script>

<template>
  <div class="join-page -m-6 min-h-full p-6">
    <div class="mx-auto max-w-lg space-y-6">
      <!-- Hero banner -->
      <div class="join-hero overflow-hidden rounded-xl" aria-hidden="true" />

      <!-- Page header -->
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-bold text-white">Join Session</h1>
      </div>

      <!-- Loading state -->
      <div
        v-if="store.loading && !store.currentSession"
        class="flex items-center justify-center py-20"
        aria-live="polite"
        aria-busy="true"
      >
        <span class="text-gray-400">Loading session…</span>
      </div>

      <!-- Error loading session -->
      <div
        v-else-if="store.error && !store.currentSession"
        role="alert"
        class="not-found-panel flex min-h-[14rem] flex-col items-center justify-center rounded-xl px-6 py-8 text-center"
      >
        <p class="mb-2 text-lg font-semibold text-red-300">Session not found</p>
        <p class="text-sm text-red-400">{{ store.error }}</p>
        <RouterLink
          to="/"
          class="mt-4 inline-block text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        >
          ← Back to home
        </RouterLink>
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

      <!-- Join form + QR hand-off -->
      <template v-else-if="store.currentSession">
        <PlayerJoinForm
          :session-id="sessionId"
          :session-name="store.currentSession.name"
          @joined="handleJoined"
        />

        <!-- Join by QR card: QR over the left 35%, heading/subtext over the right 65% -->
        <div class="qr-card flex items-center gap-4 rounded-xl p-5">
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
      </template>
    </div>
  </div>
</template>

<style scoped>
.join-page {
  background-image: url('/assets/join-session/join-session-background.png');
  background-size: cover;
  background-position: center;
  background-attachment: fixed;
}

.join-hero {
  height: 160px;
  background-image: url('/assets/join-session/join-session-hero-banner.png');
  background-size: cover;
  background-position: center;
}

.not-found-panel {
  background-image: url('/assets/join-session/session-not-found-error-panel.png');
  background-size: cover;
  background-position: left center;
}

.qr-card {
  background-image: url('/assets/join-session/join-by-qr-card.png');
  background-size: cover;
  background-position: left center;
}
</style>

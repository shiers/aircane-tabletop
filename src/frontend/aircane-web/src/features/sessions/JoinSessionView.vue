<script setup lang="ts">
/**
 * JoinSessionView — the page players open when they follow a join URL.
 * Route: /join/:sessionId
 *
 * Loads the session details, then shows the PlayerJoinForm.
 * On successful join, shows a waiting/confirmation state.
 */
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useSessionStore } from './store'
import PlayerJoinForm from './PlayerJoinForm.vue'
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
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-lg items-center">
        <h1 class="text-xl font-bold tracking-tight text-aircane-400">Aircane Tabletop</h1>
      </div>
    </header>

    <div class="mx-auto max-w-lg px-6 py-10">
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
        class="rounded-xl border border-red-800 bg-red-950 px-6 py-8 text-center"
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

      <!-- Joined — waiting for approval -->
      <div
        v-else-if="joinResult"
        class="rounded-xl border border-gray-800 bg-gray-900 p-8 text-center"
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

      <!-- Join form -->
      <PlayerJoinForm
        v-else-if="store.currentSession"
        :session-id="sessionId"
        :session-name="store.currentSession.name"
        @joined="handleJoined"
      />
    </div>
  </main>
</template>

<script setup lang="ts">
/**
 * JoinSessionView - the page players open when they follow a join URL.
 * Route: /join/:sessionId
 *
 * Loads the session details, then shows the PlayerJoinForm. After joining, a player the host has
 * not approved yet waits on this page (WaitingForApproval) and moves on to the session view the
 * moment they are approved, without a reload. A player who comes back here (a reload, a dropped
 * connection) is recognised by the participant token the page remembered and resumes where they
 * were; a token the server no longer accepts is dropped and the form is shown as for anyone new.
 */
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './store'
import PlayerJoinForm from './PlayerJoinForm.vue'
import WaitingForApproval from './components/WaitingForApproval.vue'
import { getJoinInfo, reconnectSession, type JoinSessionResult } from './api'
import {
  clearParticipantSession,
  loadLiveParticipantSession,
  loadParticipantSessionFor,
  type ParticipantSession,
} from './participantSession'
import { isParticipantDenied, isTokenRejected } from './playerErrors'

// ---------------------------------------------------------------------------
// Route params
// ---------------------------------------------------------------------------

const route = useRoute()
const router = useRouter()
const sessionId = (route.params.sessionId as string | undefined) ?? ''

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const store = useSessionStore()

/** Who this browser is at the table once they have joined (or are being recognised after a reload). */
const participant = ref<{ participantId: string; displayName: string } | null>(null)

/**
 * Dedicated lookup state, tracked locally and independent of the shared store.
 * This keeps a failed session lookup from being confused with a join-submit
 * error (store.error) or with stale session state the store may still hold.
 */
const lookupLoading = ref(false)
const lookupError = ref<string | null>(null)

/** The session's name from the anonymous join read; null until it is known to be joinable. */
const sessionName = ref<string | null>(null)

// ---------------------------------------------------------------------------
// Load session info
// ---------------------------------------------------------------------------

/** The table this browser is already at, when the page was opened with no session of its own (/sessions). */
const returningTo = ref<ParticipantSession | null>(null)

/**
 * "Join Session" in the sidebar opens /sessions, which has no table in it. For a player who is already at one,
 * a blank join form looks like having been thrown out. So: find out where they stand, and take them back
 * (approved: the session; waiting or declined: the join page for it, which shows that), or, if the server no
 * longer accepts the login, forget it and show the page as for anyone new. While that is being found out, or if the
 * host cannot be reached, a "Return to your session" card is shown instead of nothing.
 */
async function returnToStoredSession(): Promise<void> {
  const saved = loadLiveParticipantSession()
  if (!saved) return
  returningTo.value = saved
  try {
    const status = await reconnectSession(saved.sessionId)
    await router?.replace(status.isApproved
      ? { name: 'play-session', params: { sessionId: saved.sessionId } }
      : { name: 'join-session', params: { sessionId: saved.sessionId } })
  } catch (err) {
    if (isParticipantDenied(err)) {
      await router?.replace({ name: 'join-session', params: { sessionId: saved.sessionId } })
    } else if (isTokenRejected(err)) {
      // Expired, revoked, or the table has ended: it must never get in the way of joining another.
      clearParticipantSession()
      returningTo.value = null
    }
    // Anything else (the host is out of reach): the card stays, with its link.
  }
}

onMounted(async () => {
  // No pre-populated session ID (the /sessions index): there is nothing to look up, so the join form always
  // renders instead of hanging on an invalid request. A player who is already at a table is taken back to it.
  if (!sessionId) {
    await returnToStoredSession()
    return
  }

  // Already joined from this tab (a reload, a phone that woke up): pick up where they were. The
  // waiting screen asks the server whether the host has approved them and moves on if so. A token
  // for another session, or one that has expired, is removed here and never gets in the way.
  const saved = loadParticipantSessionFor(sessionId)
  if (saved) {
    participant.value = { participantId: saved.participantId, displayName: saved.displayName }
    return
  }

  await lookUpSession()
})

async function lookUpSession(): Promise<void> {
  lookupLoading.value = true
  try {
    // The player has no token yet, so this reads the anonymous join-info endpoint: just the name
    // and whether the session can be joined. A missing, ended or non-joinable session look the same.
    const info = await getJoinInfo(sessionId)
    if (info.joinable) {
      sessionName.value = info.name
    } else {
      lookupError.value = 'This session was not found or is no longer open to players.'
    }
  } catch {
    // Record the failure in dedicated lookup state so the standalone
    // "Session not found" panel owns it (store.error is left for join submits).
    lookupError.value = 'Could not look up this session. Check the link and your connection.'
  } finally {
    lookupLoading.value = false
  }
}

// ---------------------------------------------------------------------------
// Join handler
// ---------------------------------------------------------------------------

function enterSession(): void {
  void router?.replace({ name: 'play-session', params: { sessionId } })
}

function handleJoined(result: JoinSessionResult): void {
  // A session that does not need the host's approval lets them straight in.
  if (result.isApproved) {
    enterSession()
    return
  }
  participant.value = { participantId: result.participantId, displayName: result.displayName }
}

/** The server no longer accepts the remembered login, or the player chose to join again after a denial. */
async function startOver(): Promise<void> {
  clearParticipantSession()
  store.error = null
  participant.value = null
  await lookUpSession()
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

      <!-- Already at a table: a way back, not a blank form -->
      <div
        v-else-if="returningTo"
        class="rounded-xl border border-aircane-500 bg-aircane-900/30 p-5 text-center"
        data-testid="return-to-session"
      >
        <h2 class="text-lg font-semibold text-white">Return to your session</h2>
        <p class="mt-1 text-sm text-gray-300">
          You are at a table as {{ returningTo.displayName }}.
        </p>
        <RouterLink
          :to="{ name: 'play-session', params: { sessionId: returningTo.sessionId } }"
          class="mt-4 inline-flex min-h-[44px] items-center justify-center rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        >
          Back to the table
        </RouterLink>
      </div>

      <!-- Joined - waiting for the host to approve; moves on by itself -->
      <WaitingForApproval
        v-else-if="participant"
        :session-id="sessionId"
        :participant-id="participant.participantId"
        :display-name="participant.displayName"
        @approved="enterSession"
        @rejected="startOver"
        @rejoin="startOver"
      />

      <!-- Join form. It always renders first (regardless of whether the session
           loaded), with the error panel shown BELOW it. -->
      <template v-else>
        <PlayerJoinForm
          :session-id="sessionId"
          :session-name="lookupError ? undefined : (sessionName ?? undefined)"
          :hide-error="!!lookupError"
          @joined="handleJoined"
        />

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

</style>

<script setup lang="ts">
/**
 * PlayerJoinForm - form for a player to join a session.
 * Accepts a display name and invite code, then emits the join result.
 */
import { ref, computed } from 'vue'
import { useSessionStore } from './store'
import type { JoinSessionResult } from './api'
import DisplayNameField from './components/DisplayNameField.vue'
import InviteCodeField from './components/InviteCodeField.vue'

// ---------------------------------------------------------------------------
// Props & emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  sessionId: string
  /** Optional session name to display in the heading. */
  sessionName?: string
}>()

const emit = defineEmits<{
  /** Emitted when the player successfully joins. */
  joined: [result: JoinSessionResult]
}>()

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

const store = useSessionStore()

const displayName = ref('')
const inviteCode = ref('')
const fieldErrors = ref<{ displayName?: string; inviteCode?: string }>({})

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

function validate(): boolean {
  fieldErrors.value = {}
  if (!displayName.value.trim()) {
    fieldErrors.value.displayName = 'Display name is required.'
  } else if (displayName.value.trim().length > 50) {
    fieldErrors.value.displayName = 'Display name must be 50 characters or fewer.'
  }
  if (!inviteCode.value.trim()) {
    fieldErrors.value.inviteCode = 'Invite code is required.'
  }
  return Object.keys(fieldErrors.value).length === 0
}

const isValid = computed(
  () => displayName.value.trim().length > 0 && inviteCode.value.trim().length > 0,
)

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

async function handleSubmit(): Promise<void> {
  if (!validate()) return
  try {
    const result = await store.joinSession(props.sessionId, {
      displayName: displayName.value.trim(),
      inviteCode: inviteCode.value.trim(),
    })
    emit('joined', result)
  } catch {
    // store.error is set by the store action
  }
}
</script>

<template>
  <section
    aria-labelledby="join-form-heading"
    class="join-panel rounded-xl p-6"
    v-bg-asset="{ url: '/assets/join-session/join-session-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
  >
    <h2 id="join-form-heading" class="mb-1 text-lg font-semibold text-white">
      Join Session
    </h2>
    <p v-if="sessionName" class="mb-5 text-sm text-gray-400">
      {{ sessionName }}
    </p>
    <p v-else class="mb-5 text-sm text-gray-400">
      Enter your display name and the invite code to join.
    </p>

    <!-- API error banner -->
    <div
      v-if="store.error"
      role="alert"
      class="mb-4 flex items-start gap-3 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
    >
      <svg
        class="mt-0.5 h-4 w-4 shrink-0 text-red-400"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zm-.75-9.25a.75.75 0 011.5 0v3.5a.75.75 0 01-1.5 0v-3.5zm.75 6a.75.75 0 100-1.5.75.75 0 000 1.5z"
          clip-rule="evenodd"
        />
      </svg>
      <span>{{ store.error }}</span>
    </div>

    <form novalidate @submit.prevent="handleSubmit">
      <!-- Display name -->
      <div class="mb-4">
        <label
          for="join-display-name"
          class="mb-1 block text-sm font-medium text-gray-300"
        >
          Display Name <span class="text-red-400" aria-hidden="true">*</span>
        </label>
        <DisplayNameField
          id="join-display-name"
          v-model="displayName"
          :error="fieldErrors.displayName"
        />
      </div>

      <!-- Invite code -->
      <div class="mb-6">
        <label
          for="join-invite-code"
          class="mb-1 block text-sm font-medium text-gray-300"
        >
          Invite Code <span class="text-red-400" aria-hidden="true">*</span>
        </label>
        <InviteCodeField
          id="join-invite-code"
          v-model="inviteCode"
          :error="fieldErrors.inviteCode"
        />
      </div>

      <!-- Submit -->
      <button
        type="submit"
        class="join-submit w-full rounded-lg px-4 py-2.5 text-sm font-semibold text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-opacity"
        v-bg-asset="{ url: '/assets/join-session/join-session-button-art.png', fallback: 'transparent', size: '100% 100%' }"
        :disabled="store.loading || !isValid"
      >
        <span v-if="store.loading">Joining…</span>
        <span v-else>Join Session →</span>
      </button>
    </form>
  </section>
</template>

<style scoped>
.join-panel {
  border: var(--border-gold);
  background-size: cover;
  background-position: left center;
}

/* Join button — art-independent CSS base so it stays usable with no art. */
.join-submit {
  border: var(--border-gold);
  background-color: var(--color-purple);
  box-shadow: var(--glow-purple);
  background-size: 100% 100%;
}
</style>

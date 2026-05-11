<script setup lang="ts">
/**
 * PlayerJoinForm — form for a player to join a session.
 * Accepts a display name and invite code, then emits the join result.
 */
import { ref, computed } from 'vue'
import { useSessionStore } from './store'
import type { JoinSessionResult } from './api'

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
    class="rounded-xl border border-gray-800 bg-gray-900 p-6"
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
        <input
          id="join-display-name"
          v-model="displayName"
          type="text"
          autocomplete="nickname"
          maxlength="50"
          placeholder="e.g. Thorin Oakenshield"
          class="w-full rounded-md border bg-gray-800 px-3 py-2 text-sm text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          :class="fieldErrors.displayName ? 'border-red-600' : 'border-gray-700'"
          :aria-describedby="fieldErrors.displayName ? 'join-display-name-error' : undefined"
          :aria-invalid="!!fieldErrors.displayName"
        />
        <p
          v-if="fieldErrors.displayName"
          id="join-display-name-error"
          role="alert"
          class="mt-1 text-xs text-red-400"
        >
          {{ fieldErrors.displayName }}
        </p>
      </div>

      <!-- Invite code -->
      <div class="mb-6">
        <label
          for="join-invite-code"
          class="mb-1 block text-sm font-medium text-gray-300"
        >
          Invite Code <span class="text-red-400" aria-hidden="true">*</span>
        </label>
        <input
          id="join-invite-code"
          v-model="inviteCode"
          type="text"
          autocomplete="off"
          autocorrect="off"
          autocapitalize="characters"
          spellcheck="false"
          placeholder="e.g. ABC123"
          class="w-full rounded-md border bg-gray-800 px-3 py-2 text-center font-mono text-lg tracking-widest text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          :class="fieldErrors.inviteCode ? 'border-red-600' : 'border-gray-700'"
          :aria-describedby="fieldErrors.inviteCode ? 'join-invite-code-error' : undefined"
          :aria-invalid="!!fieldErrors.inviteCode"
        />
        <p
          v-if="fieldErrors.inviteCode"
          id="join-invite-code-error"
          role="alert"
          class="mt-1 text-xs text-red-400"
        >
          {{ fieldErrors.inviteCode }}
        </p>
      </div>

      <!-- Submit -->
      <button
        type="submit"
        class="w-full rounded-lg bg-aircane-600 px-4 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
        :disabled="store.loading || !isValid"
      >
        <span v-if="store.loading">Joining…</span>
        <span v-else>Join Session</span>
      </button>
    </form>
  </section>
</template>

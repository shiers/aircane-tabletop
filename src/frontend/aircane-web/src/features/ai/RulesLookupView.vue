<script setup lang="ts">
import { ref } from 'vue'
import {
  askRulesQuestion,
  type RulesQuestionRequest,
  type RulesQuestionResponse,
} from './api'
import LicenseAttributionModal from '@/features/library/components/LicenseAttributionModal.vue'
import GameSystemDropdown from './components/GameSystemDropdown.vue'

const licensesModalOpen = ref(false)

// Game system options for the dropdown filter. The id doubles as the value sent to the backend,
// preserving the existing free-text request shape (gameSystem is a plain string on the request).
const gameSystemOptions = [
  { id: 'D&D 5e 2014', name: 'D&D 5e 2014' },
  { id: 'Pathfinder 2e', name: 'Pathfinder 2e' },
  { id: 'Generic Freeform', name: 'Generic Freeform' },
]

/** Tailwind classes for a citation license badge by license key. */
function licenseBadgeClasses(licenseKey: string): string {
  switch (licenseKey) {
    case 'cc-by-4.0':
      return 'bg-teal-900 text-teal-300'
    case 'orc':
      return 'bg-purple-900 text-purple-300'
    default:
      return 'bg-gray-800 text-gray-300'
  }
}

const question = ref('')
const gameSystem = ref<string | null>(null)
const ruleset = ref('')
const loading = ref(false)
const error = ref<string | null>(null)
const result = ref<RulesQuestionResponse | null>(null)

async function submitQuestion() {
  const trimmed = question.value.trim()
  if (!trimmed) return

  loading.value = true
  error.value = null
  result.value = null

  const request: RulesQuestionRequest = {
    question: trimmed,
    gameSystem: gameSystem.value?.trim() || null,
    ruleset: ruleset.value.trim() || null,
  }

  try {
    result.value = await askRulesQuestion(request)
  } catch (e: any) {
    error.value =
      e?.response?.data?.error ?? e?.message ?? 'Failed to get an answer. Please try again.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="page-sections">
    <!-- Hero banner -->
    <div
      class="app-hero"
      v-bg-asset="{ url: '/assets/rules/rules-lookup-hero-banner.png', fallback: '#0d0d2a' }"
      role="img"
      aria-label="Rules Lookup"
    >
      <div class="app-hero__overlay">
        <h1 class="app-hero__title">Rules Lookup</h1>
        <p class="app-hero__subtitle">
          Ask a rules question and get an AI-generated answer grounded in your indexed sources.
        </p>
      </div>
    </div>

    <!-- Question + results column -->
    <div class="page-sections">
      <!-- Question panel: ask-question-panel-art backs the panel, controls over the right 65% -->
      <div
        class="ask-question-panel rounded-lg bg-cover bg-left p-5"
        v-bg-asset="{ url: '/assets/rules/ask-question-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
      >
      <div class="ml-auto w-[65%] min-w-[260px]">

      <!-- Question form -->
      <form @submit.prevent="submitQuestion" class="space-y-4">
        <div>
          <label for="rules-question" class="block text-sm font-medium text-gray-300 mb-1">
            Your Question
          </label>
          <textarea
            id="rules-question"
            v-model="question"
            rows="3"
            placeholder="e.g. How does grappling work in D&D 5e?"
            class="w-full rounded-lg border border-surface-700 bg-surface-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-600 focus:ring-2 focus:ring-aircane-600/20 focus:outline-none"
            :disabled="loading"
          ></textarea>
        </div>

        <!-- Optional filters -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <label for="game-system" class="block text-sm font-medium text-gray-300 mb-1">
              Game System <span class="text-gray-500">(optional)</span>
            </label>
            <GameSystemDropdown
              id="game-system"
              v-model="gameSystem"
              :options="gameSystemOptions"
              :disabled="loading"
            />
          </div>
          <div>
            <label for="ruleset-filter" class="block text-sm font-medium text-gray-300 mb-1">
              Ruleset <span class="text-gray-500">(optional)</span>
            </label>
            <input
              id="ruleset-filter"
              v-model="ruleset"
              type="text"
              placeholder="e.g. PHB, DMG"
              class="w-full rounded-lg border border-surface-700 bg-surface-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-600 focus:ring-2 focus:ring-aircane-600/20 focus:outline-none"
              :disabled="loading"
            />
          </div>
        </div>

        <button
          type="submit"
          :disabled="loading || !question.trim()"
          class="ask-question-button rounded-md px-5 py-2.5 text-sm font-semibold text-white shadow focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          v-bg-asset="{ url: '/assets/rules/ask-question-button-.png', fallback: 'transparent', size: '100% 100%' }"
        >
          <span v-if="loading" class="flex items-center gap-2">
            <svg
              class="h-4 w-4 animate-spin"
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <circle
                class="opacity-25"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                stroke-width="4"
              />
              <path
                class="opacity-75"
                fill="currentColor"
                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
              />
            </svg>
            Searching sources…
          </span>
          <span v-else>Ask Question</span>
        </button>
      </form>
      </div>
      </div>

      <!-- Error banner -->
      <div
        v-if="error"
        class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-red-300"
        role="alert"
      >
        <p class="font-medium">Something went wrong</p>
        <p class="mt-1 text-sm">{{ error }}</p>
      </div>

      <!-- Result -->
      <div v-if="result" class="space-y-6">
        <!-- Uncertainty warning -->
        <output
          v-if="!result.hasSourceSupport"
          class="block rounded-md border border-yellow-700 bg-yellow-950 px-4 py-3 text-yellow-300"
        >
          <div class="flex items-start gap-2">
            <svg
              class="mt-0.5 h-5 w-5 flex-shrink-0"
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
              stroke-width="1.5"
              stroke="currentColor"
              aria-hidden="true"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z"
              />
            </svg>
            <div>
              <p class="font-medium">Unverified Answer</p>
              <p class="mt-0.5 text-sm text-yellow-400">
                This answer could not be confirmed from your indexed source documents. It may be
                based on general knowledge and could contain inaccuracies. For reliable, grounded
                answers, import the relevant rulebook into your library and ask again.
              </p>
            </div>
          </div>
        </output>

        <!-- Answer -->
        <div class="rounded-lg border border-surface-700/50 bg-surface-850 p-5">
          <h2 class="mb-3 text-lg font-semibold text-white">Answer</h2>
          <div class="whitespace-pre-wrap text-gray-300 leading-relaxed">{{ result.answer }}</div>
        </div>

        <!-- Citations -->
        <div v-if="result.citations.length > 0">
          <h2 class="mb-3 text-lg font-semibold text-white">Sources</h2>
          <ul class="space-y-3">
            <!-- Each source renders over citation-card-art, with the scroll icon far-left -->
            <li
              v-for="(citation, idx) in result.citations"
              :key="citation.chunkId"
              class="citation-card flex items-center gap-3 rounded-lg bg-cover bg-left p-4"
              v-bg-asset="{ url: '/assets/rules/citation-card-art.png', fallback: '#0d0d2a', position: 'left center' }"
            >
              <!-- Scroll icon (decorative) far-left -->
              <img
                src="/assets/rules/rules-scroll-thumbnail.png"
                alt=""
                aria-hidden="true"
                class="h-10 w-10 flex-shrink-0"
                style="object-fit: contain"
                @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
              />
              <!-- Citation text over the right 65% -->
              <div class="ml-auto w-[65%] min-w-[200px] text-sm text-gray-300">
                <span class="flex flex-wrap items-center gap-1.5">
                  <span class="flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-gray-800 text-xs font-medium text-gray-300">
                    {{ idx + 1 }}
                  </span>
                  <span class="font-medium text-gray-100">{{ citation.sourceTitle }}</span>
                  <span v-if="citation.pageNumber"> - p. {{ citation.pageNumber }}</span>
                  <span v-if="citation.sectionTitle"> · {{ citation.sectionTitle }}</span>
                  <!-- License badge (built-in sources only); opens the attribution modal -->
                  <button
                    v-if="citation.licenseKey"
                    type="button"
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium hover:opacity-80 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                    :class="licenseBadgeClasses(citation.licenseKey)"
                    :title="`${citation.licenseDisplayName ?? citation.licenseKey} — view attribution`"
                    @click="licensesModalOpen = true"
                  >
                    {{ citation.licenseDisplayName ?? citation.licenseKey }}
                  </button>
                </span>
              </div>
            </li>
          </ul>
        </div>

        <!-- No citations note -->
        <p
          v-if="result.citations.length === 0 && result.hasSourceSupport"
          class="text-sm text-gray-500"
        >
          No specific citations were returned for this answer.
        </p>
      </div>
    </div>

    <!-- Open-content license attribution modal -->
    <LicenseAttributionModal :open="licensesModalOpen" @close="licensesModalOpen = false" />
  </div>
</template>

<style scoped>
/* Type C panel/card — gold border so they stay bordered/legible with no art. */
.ask-question-panel,
.citation-card {
  border: var(--border-gold);
}

/* Type D button — art-independent CSS base so it is always clickable. */
.ask-question-button {
  border: var(--border-gold);
  background-color: var(--color-purple);
  box-shadow: var(--glow-purple);
  background-repeat: no-repeat;
}
</style>

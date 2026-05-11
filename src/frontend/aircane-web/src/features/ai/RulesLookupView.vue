<script setup lang="ts">
import { ref } from 'vue'
import {
  askRulesQuestion,
  type RulesQuestionRequest,
  type RulesQuestionResponse,
} from './api'

const question = ref('')
const gameSystem = ref('')
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
    gameSystem: gameSystem.value.trim() || null,
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
  <div class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-3xl items-center justify-between">
        <div class="flex items-center gap-3">
          <RouterLink
            to="/"
            class="text-sm text-gray-400 hover:text-white"
            aria-label="Back to home"
          >
            ← Home
          </RouterLink>
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Rules Lookup</h1>
        </div>
      </div>
    </header>

    <main class="mx-auto max-w-3xl px-6 py-8">
      <p class="mb-6 text-gray-400">
        Ask a rules question and get an AI-generated answer grounded in your indexed source
        documents.
      </p>

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
            class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
            :disabled="loading"
          ></textarea>
        </div>

        <!-- Optional filters -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <label for="game-system" class="block text-sm font-medium text-gray-300 mb-1">
              Game System <span class="text-gray-500">(optional)</span>
            </label>
            <input
              id="game-system"
              v-model="gameSystem"
              type="text"
              placeholder="e.g. D&D 5e 2014"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
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
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            />
          </div>
        </div>

        <button
          type="submit"
          :disabled="loading || !question.trim()"
          class="rounded-md bg-aircane-600 px-5 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
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

      <!-- Error banner -->
      <div
        v-if="error"
        class="mt-6 rounded-md border border-red-800 bg-red-950 px-4 py-3 text-red-300"
        role="alert"
      >
        <p class="font-medium">Something went wrong</p>
        <p class="mt-1 text-sm">{{ error }}</p>
      </div>

      <!-- Result -->
      <div v-if="result" class="mt-8 space-y-6">
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
        <div class="rounded-lg border border-gray-800 bg-gray-900 p-5">
          <h2 class="mb-3 text-lg font-semibold text-white">Answer</h2>
          <div class="whitespace-pre-wrap text-gray-300 leading-relaxed">{{ result.answer }}</div>
        </div>

        <!-- Citations -->
        <div v-if="result.citations.length > 0" class="rounded-lg border border-gray-800 bg-gray-900 p-5">
          <h2 class="mb-3 text-lg font-semibold text-white">Sources</h2>
          <ul class="space-y-2">
            <li
              v-for="(citation, idx) in result.citations"
              :key="citation.chunkId"
              class="flex items-start gap-2 text-sm text-gray-400"
            >
              <span class="mt-0.5 flex h-5 w-5 flex-shrink-0 items-center justify-center rounded-full bg-gray-800 text-xs font-medium text-gray-300">
                {{ idx + 1 }}
              </span>
              <span>
                <span class="font-medium text-gray-200">{{ citation.sourceTitle }}</span>
                <span v-if="citation.pageNumber"> — p. {{ citation.pageNumber }}</span>
                <span v-if="citation.sectionTitle"> · {{ citation.sectionTitle }}</span>
              </span>
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
    </main>
  </div>
</template>

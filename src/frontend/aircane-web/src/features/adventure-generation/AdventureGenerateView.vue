<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  generateAdventure,
  type GenerateAdventureRequest,
  type GenerateAdventureResponse,
  type AdventureMode,
} from './api'

const mode = ref<AdventureMode>('Solo')
const ruleset = ref('D&D 5e 2014')
const gameSystem = ref('D&D 5e 2014')
const partySize = ref(4)
const averageLevel = ref(1)
const tone = ref('epic')
const length = ref('one-shot')
const difficulty = ref('medium')
const combatRatio = ref(34)
const explorationRatio = ref(33)
const roleplayRatio = ref(33)
const setting = ref('')

const loading = ref(false)
const error = ref<string | null>(null)
const result = ref<GenerateAdventureResponse | null>(null)

const toneOptions = ['dark', 'lighthearted', 'epic', 'horror', 'comedic', 'mysterious', 'heroic']
const lengthOptions = ['one-shot', 'short', 'medium', 'long']
const difficultyOptions = ['easy', 'medium', 'hard', 'deadly']

const ratioSum = computed(() => combatRatio.value + explorationRatio.value + roleplayRatio.value)
const ratioValid = computed(() => Math.abs(ratioSum.value - 100) <= 5)

// Auto-adjust roleplay ratio when combat or exploration changes
function adjustRoleplay() {
  const remaining = 100 - combatRatio.value - explorationRatio.value
  if (remaining >= 0 && remaining <= 100) {
    roleplayRatio.value = remaining
  }
}

watch([combatRatio, explorationRatio], adjustRoleplay)

async function submitForm() {
  loading.value = true
  error.value = null
  result.value = null

  const request: GenerateAdventureRequest = {
    mode: mode.value,
    ruleset: ruleset.value.trim(),
    gameSystem: gameSystem.value.trim(),
    partySize: partySize.value,
    averageLevel: averageLevel.value,
    tone: tone.value,
    length: length.value,
    difficulty: difficulty.value,
    combatRatio: combatRatio.value,
    explorationRatio: explorationRatio.value,
    roleplayRatio: roleplayRatio.value,
    setting: setting.value.trim() || null,
    characterIds: null,
  }

  try {
    result.value = await generateAdventure(request)
  } catch (e: any) {
    const data = e?.response?.data
    if (data?.errors) {
      const messages = Object.values(data.errors).flat()
      error.value = (messages as string[]).join(' ')
    } else {
      error.value = data?.detail ?? e?.message ?? 'Failed to start adventure generation.'
    }
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
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Generate Adventure</h1>
        </div>
      </div>
    </header>

    <main class="mx-auto max-w-3xl px-6 py-8">
      <p class="mb-6 text-gray-400">
        Configure your adventure parameters and let the AI generate a playable adventure tailored to
        your party.
      </p>

      <!-- Success result -->
      <output
        v-if="result"
        class="mb-6 block rounded-md border border-green-800 bg-green-950 px-4 py-3 text-green-300"
      >
        <p class="font-medium">Adventure generation started!</p>
        <p class="mt-1 text-sm">Job ID: {{ result.jobId }}</p>
        <p class="mt-1 text-sm">{{ result.message }}</p>
      </output>

      <!-- Error banner -->
      <div
        v-if="error"
        class="mb-6 rounded-md border border-red-800 bg-red-950 px-4 py-3 text-red-300"
        role="alert"
      >
        <p class="font-medium">Validation Error</p>
        <p class="mt-1 text-sm">{{ error }}</p>
      </div>

      <form @submit.prevent="submitForm" class="space-y-6">
        <!-- Mode selector -->
        <fieldset>
          <legend class="block text-sm font-medium text-gray-300 mb-2">Adventure Mode</legend>
          <div class="flex gap-4">
            <label
              class="flex cursor-pointer items-center gap-2 rounded-md border px-4 py-2 transition-colors"
              :class="
                mode === 'Solo'
                  ? 'border-aircane-500 bg-aircane-950 text-aircane-300'
                  : 'border-gray-700 bg-gray-900 text-gray-400 hover:border-gray-600'
              "
            >
              <input
                type="radio"
                v-model="mode"
                value="Solo"
                class="sr-only"
                :disabled="loading"
              />
              <span class="text-sm font-medium">Solo</span>
            </label>
            <label
              class="flex cursor-pointer items-center gap-2 rounded-md border px-4 py-2 transition-colors"
              :class="
                mode === 'Group'
                  ? 'border-aircane-500 bg-aircane-950 text-aircane-300'
                  : 'border-gray-700 bg-gray-900 text-gray-400 hover:border-gray-600'
              "
            >
              <input
                type="radio"
                v-model="mode"
                value="Group"
                class="sr-only"
                :disabled="loading"
              />
              <span class="text-sm font-medium">Group</span>
            </label>
          </div>
        </fieldset>

        <!-- Ruleset and Game System -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div>
            <label for="ruleset" class="block text-sm font-medium text-gray-300 mb-1">
              Ruleset
            </label>
            <input
              id="ruleset"
              v-model="ruleset"
              type="text"
              placeholder="e.g. D&D 5e 2014"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            />
          </div>
          <div>
            <label for="game-system" class="block text-sm font-medium text-gray-300 mb-1">
              Game System
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
        </div>

        <!-- Party Size (Group mode) and Level -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div v-if="mode === 'Group'">
            <label for="party-size" class="block text-sm font-medium text-gray-300 mb-1">
              Party Size
            </label>
            <input
              id="party-size"
              v-model.number="partySize"
              type="number"
              min="2"
              max="10"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            />
          </div>
          <div>
            <label for="average-level" class="block text-sm font-medium text-gray-300 mb-1">
              {{ mode === 'Solo' ? 'Character Level' : 'Average Party Level' }}
            </label>
            <input
              id="average-level"
              v-model.number="averageLevel"
              type="number"
              min="1"
              max="20"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            />
          </div>
        </div>

        <!-- Tone, Length, Difficulty -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <div>
            <label for="tone" class="block text-sm font-medium text-gray-300 mb-1">Tone</label>
            <select
              id="tone"
              v-model="tone"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            >
              <option v-for="t in toneOptions" :key="t" :value="t">
                {{ t.charAt(0).toUpperCase() + t.slice(1) }}
              </option>
            </select>
          </div>
          <div>
            <label for="length" class="block text-sm font-medium text-gray-300 mb-1">Length</label>
            <select
              id="length"
              v-model="length"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            >
              <option v-for="l in lengthOptions" :key="l" :value="l">
                {{ l.charAt(0).toUpperCase() + l.slice(1) }}
              </option>
            </select>
          </div>
          <div>
            <label for="difficulty" class="block text-sm font-medium text-gray-300 mb-1">
              Difficulty
            </label>
            <select
              id="difficulty"
              v-model="difficulty"
              class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            >
              <option v-for="d in difficultyOptions" :key="d" :value="d">
                {{ d.charAt(0).toUpperCase() + d.slice(1) }}
              </option>
            </select>
          </div>
        </div>

        <!-- Content Ratios -->
        <fieldset>
          <legend class="block text-sm font-medium text-gray-300 mb-3">
            Content Ratios
            <span
              class="ml-2 text-xs"
              :class="ratioValid ? 'text-green-400' : 'text-red-400'"
            >
              (Sum: {{ ratioSum }}%)
            </span>
          </legend>

          <div class="space-y-4">
            <div>
              <div class="flex items-center justify-between mb-1">
                <label for="combat-ratio" class="text-sm text-gray-400">Combat</label>
                <span class="text-sm text-gray-300 font-mono">{{ combatRatio }}%</span>
              </div>
              <input
                id="combat-ratio"
                v-model.number="combatRatio"
                type="range"
                min="0"
                max="100"
                step="5"
                class="w-full accent-aircane-500"
                :disabled="loading"
              />
            </div>

            <div>
              <div class="flex items-center justify-between mb-1">
                <label for="exploration-ratio" class="text-sm text-gray-400">Exploration</label>
                <span class="text-sm text-gray-300 font-mono">{{ explorationRatio }}%</span>
              </div>
              <input
                id="exploration-ratio"
                v-model.number="explorationRatio"
                type="range"
                min="0"
                max="100"
                step="5"
                class="w-full accent-aircane-500"
                :disabled="loading"
              />
            </div>

            <div>
              <div class="flex items-center justify-between mb-1">
                <label for="roleplay-ratio" class="text-sm text-gray-400">Roleplay</label>
                <span class="text-sm text-gray-300 font-mono">{{ roleplayRatio }}%</span>
              </div>
              <input
                id="roleplay-ratio"
                v-model.number="roleplayRatio"
                type="range"
                min="0"
                max="100"
                step="5"
                class="w-full accent-aircane-500"
                :disabled="loading"
              />
            </div>
          </div>

          <p v-if="!ratioValid" class="mt-2 text-xs text-red-400">
            Ratios must sum to approximately 100% (currently {{ ratioSum }}%).
          </p>
        </fieldset>

        <!-- Setting (optional) -->
        <div>
          <label for="setting" class="block text-sm font-medium text-gray-300 mb-1">
            Setting <span class="text-gray-500">(optional)</span>
          </label>
          <input
            id="setting"
            v-model="setting"
            type="text"
            placeholder="e.g. Forgotten Realms, Eberron, custom world..."
            class="w-full rounded-md border border-gray-700 bg-gray-900 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
            :disabled="loading"
          />
        </div>

        <!-- Submit -->
        <button
          type="submit"
          :disabled="loading || !ratioValid"
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
            Generating…
          </span>
          <span v-else>Generate Adventure</span>
        </button>
      </form>
    </main>
  </div>
</template>

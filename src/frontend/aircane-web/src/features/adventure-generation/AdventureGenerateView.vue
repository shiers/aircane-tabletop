<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { useAdventureGenerationStore } from './store'
import { useCampaignStore } from '@/features/campaigns/store'
import { apiErrorMessage, apiValidationMessage } from '@/shared/api/errors'
import {
  type GenerateAdventureRequest,
  type AdventureMode,
} from './api'
import ModeToggle from './components/ModeToggle.vue'
import ContentRatioSlider from './components/ContentRatioSlider.vue'

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

const store = useAdventureGenerationStore()
const route = useRoute()
const campaignStore = useCampaignStore()

// Arriving from a campaign's page (/adventures/generate?campaignId=...): the adventure is generated
// for, and saved against, that campaign.
const campaignId = computed(() => {
  const q = route.query.campaignId
  return typeof q === 'string' && q ? q : null
})
const campaignName = computed(() => campaignStore.campaigns.find((c) => c.id === campaignId.value)?.name ?? null)
onMounted(() => {
  if (campaignId.value && !campaignStore.campaigns.length) void campaignStore.fetchCampaigns().catch(() => {})
})
// True while the request is being posted or a generation job is running (form locked either way).
const submitting = ref(false)
const loading = computed(() => submitting.value || store.isGenerating)
const error = ref<string | null>(null)

const toneOptions = ['dark', 'lighthearted', 'epic', 'horror', 'comedic', 'mysterious', 'heroic']
const lengthOptions = ['one-shot', 'short', 'medium', 'long']
const difficultyOptions = ['easy', 'medium', 'hard', 'deadly']

// Bridge the ModeToggle's 'solo' | 'group' with the view/API's 'Solo' | 'Group'.
const toggleMode = computed<'solo' | 'group'>({
  get: () => (mode.value === 'Group' ? 'group' : 'solo'),
  set: (value) => {
    mode.value = value === 'group' ? 'Group' : 'Solo'
  },
})

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
  if (loading.value) return
  submitting.value = true
  error.value = null

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
    await store.start(request, campaignId.value)
  } catch (e) {
    error.value = apiValidationMessage(e) ?? apiErrorMessage(e, 'detail', 'Failed to start adventure generation.')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="forge-page">
    <div class="page-sections">
      <!-- Hero banner -->
      <div
        class="app-hero"
        v-bg-asset="{ url: '/assets/adventure-forge/adventure-forge-hero-banner.png', fallback: '#0d0d2a' }"
      >
        <div class="app-hero__overlay">
          <h1 class="app-hero__title">Generate Adventure</h1>
          <p class="app-hero__subtitle">
            Configure your adventure parameters and let the AI generate a playable adventure
            tailored to your party.
          </p>
        </div>
      </div>

      <!-- Generation in progress (also shown when returning to this page mid-generation) -->
      <output
        v-if="store.isGenerating"
        class="block rounded-md border border-aircane-700 bg-surface-850 px-4 py-3 text-gray-200"
        data-testid="forge-progress"
      >
        <p class="font-medium">⚔ Generating your adventure...</p>
        <p class="mt-1 text-sm text-gray-300" data-testid="forge-progress-message">{{ store.message }}</p>
        <div class="mt-2 h-2 overflow-hidden rounded bg-gray-800">
          <div class="h-full bg-aircane-500 transition-all duration-500" :style="{ width: store.percentComplete + '%' }" />
        </div>
        <p class="mt-1 text-right text-xs text-gray-400">{{ store.percentComplete }}%</p>
      </output>

      <p v-if="campaignId" class="text-sm text-gray-300" data-testid="forge-campaign">
        Generating for campaign: <span class="font-medium text-white">{{ campaignName ?? 'this campaign' }}</span>
      </p>

      <!-- Error banner -->
      <div
        v-if="error"
        class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-red-300"
        role="alert"
      >
        <p class="font-medium">Validation Error</p>
        <p class="mt-1 text-sm">{{ error }}</p>
      </div>

      <form
        @submit.prevent="submitForm"
        class="forge-panel space-y-6 rounded-md bg-cover bg-left-top p-6 transition-opacity"
        :class="{ 'opacity-50': loading }"
        :aria-busy="loading"
        data-testid="forge-form"
        v-bg-asset="{ url: '/assets/adventure-forge/adventure-forge-panel-background.png', fallback: '#0d0d2a', position: 'left top' }"
      >
        <!-- Mode selector -->
        <fieldset>
          <legend class="block text-sm font-medium text-gray-300 mb-2">Adventure Mode</legend>
          <ModeToggle v-model="toggleMode" :disabled="loading" />
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
              class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
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
              class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
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
              class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
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
              class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:border-aircane-500 focus:ring-2 focus:ring-aircane-500 focus:outline-none"
              :disabled="loading"
            />
          </div>
        </div>

        <!-- Tone, Length, Difficulty (field art with the real select overlaid transparently) -->
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <div>
            <label for="tone" class="block text-sm font-medium text-gray-300 mb-1">Tone</label>
            <div
              class="field-art"
              v-bg-asset="{ url: '/assets/adventure-forge/tone-dropdown-art.png', fallback: '#0d0d2a', size: '100% 100%' }"
            >
              <select
                id="tone"
                v-model="tone"
                class="field-art-control"
                :disabled="loading"
              >
                <option v-for="t in toneOptions" :key="t" :value="t">
                  {{ t.charAt(0).toUpperCase() + t.slice(1) }}
                </option>
              </select>
            </div>
          </div>
          <div>
            <label for="length" class="block text-sm font-medium text-gray-300 mb-1">Length</label>
            <div
              class="field-art"
              v-bg-asset="{ url: '/assets/adventure-forge/length-dropdown-art.png', fallback: '#0d0d2a', size: '100% 100%' }"
            >
              <select
                id="length"
                v-model="length"
                class="field-art-control"
                :disabled="loading"
              >
                <option v-for="l in lengthOptions" :key="l" :value="l">
                  {{ l.charAt(0).toUpperCase() + l.slice(1) }}
                </option>
              </select>
            </div>
          </div>
          <div>
            <label for="difficulty" class="block text-sm font-medium text-gray-300 mb-1">
              Difficulty
            </label>
            <div
              class="field-art"
              v-bg-asset="{ url: '/assets/adventure-forge/difficulty-dropdown-art.png', fallback: '#0d0d2a', size: '100% 100%' }"
            >
              <select
                id="difficulty"
                v-model="difficulty"
                class="field-art-control"
                :disabled="loading"
              >
                <option v-for="d in difficultyOptions" :key="d" :value="d">
                  {{ d.charAt(0).toUpperCase() + d.slice(1) }}
                </option>
              </select>
            </div>
          </div>
        </div>

        <!-- Content Ratios -->
        <fieldset>
          <legend class="block text-sm font-medium text-gray-300 mb-3">
            Content Ratios
            <span class="ml-2 text-xs" :class="ratioValid ? 'text-green-400' : 'text-red-400'">
              (Sum: {{ ratioSum }}%)
            </span>
          </legend>

          <ContentRatioSlider
            :combat="combatRatio"
            :exploration="explorationRatio"
            :roleplay="roleplayRatio"
            :disabled="loading"
            @update:combat="combatRatio = $event"
            @update:exploration="explorationRatio = $event"
            @update:roleplay="roleplayRatio = $event"
          />

          <p v-if="!ratioValid" class="mt-2 text-xs text-red-400">
            Ratios must sum to approximately 100% (currently {{ ratioSum }}%).
          </p>
        </fieldset>

        <!-- Setting (optional) — field art with the real input overlaid transparently -->
        <div>
          <label for="setting" class="block text-sm font-medium text-gray-300 mb-1">
            Setting <span class="text-gray-500">(optional)</span>
          </label>
          <div
            class="field-art"
            v-bg-asset="{ url: '/assets/adventure-forge/setting-field-art.png', fallback: '#0d0d2a', size: '100% 100%' }"
          >
            <input
              id="setting"
              v-model="setting"
              type="text"
              placeholder="e.g. Forgotten Realms, Eberron, custom world..."
              class="field-art-control placeholder-gray-400"
              :disabled="loading"
            />
          </div>
        </div>

        <!-- Submit -->
        <button
          type="submit"
          :disabled="loading || !ratioValid"
          class="generate-button disabled:cursor-not-allowed disabled:opacity-50"
          v-bg-asset="{ url: '/assets/adventure-forge/generate-adventure-button-art.png', fallback: 'transparent', size: '100% 100%' }"
        >
          <span v-if="loading" class="flex items-center justify-center gap-2">
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
    </div>
  </div>
</template>

<style scoped>
/* Solid dark page (no full-page art); the shell owns padding/centering. */
.forge-page {
  min-height: 100%;
}

/* Field art: the real control sits transparent over the baked-in field image. */
.field-art {
  position: relative;
  width: 100%;
  background-size: 100% 100%;
  background-repeat: no-repeat;
  /* Gold-dim border so the field stays bordered/legible with no art. */
  border: var(--border-gold-dim);
  border-radius: var(--border-radius-sm);
}

.field-art-control {
  width: 100%;
  background: transparent;
  border: none;
  padding: 0.5rem 0.75rem;
  color: #f3f4f6;
  outline: none;
}

.field-art-control:focus {
  box-shadow: var(--glow-purple);
  border-radius: var(--border-radius-sm);
}

.field-art-control option {
  color: #111827;
}

/* Generate button: button art fills the element; label centred over the right 60%. */
.generate-button {
  width: 100%;
  min-height: 56px;
  background-size: 100% 100%;
  background-repeat: no-repeat;
  /* Art-independent CSS base so the button is always clickable. */
  border: var(--border-gold);
  border-radius: var(--border-radius-md);
  background-color: var(--color-purple);
  box-shadow: var(--glow-purple);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  padding-right: 20%;
  font-size: 1.125rem;
  font-weight: 700;
  color: #ffffff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.8);
}
</style>

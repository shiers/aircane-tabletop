<script setup lang="ts">
/**
 * PlaySoloLauncher - the "Play Solo" button for an approved adventure.
 *
 * Starting a run needs a campaign. There is no "current campaign" in the app, so clicking the
 * button opens a small picker: choose a campaign (or be pointed at creating one), then start.
 * Starting is idempotent on the server, so choosing a campaign that is already running this
 * adventure simply resumes it.
 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useCampaignStore } from '@/features/campaigns/store'
import { AiRole, AiAuthority } from '@/features/campaigns/api'
import { useAdventureSessionStore } from '../stores/adventureSessionStore'
import { getActiveAdventureSession } from '../api'

const props = defineProps<{
  adventureId: string
}>()

const router = useRouter()
const campaignStore = useCampaignStore()
const sessionStore = useAdventureSessionStore()

const open = ref(false)
const loadingCampaigns = ref(false)
const selectedCampaignId = ref('')
const starting = ref(false)
const error = ref<string | null>(null)
/** Set when the chosen campaign is busy with a different adventure, so it can be resumed instead. */
const resumeSessionId = ref<string | null>(null)

const campaigns = computed(() => campaignStore.campaigns)
const selectedCampaign = computed(() => campaigns.value.find((c) => c.id === selectedCampaignId.value) ?? null)

/** The AI can only advance the story itself with a DM-capable role and full authority. */
const aiCanAdvance = computed(() => {
  const c = selectedCampaign.value
  if (!c) return true
  return (
    (c.aiRole === AiRole.FullDm || c.aiRole === AiRole.Hybrid) &&
    c.aiAuthority === AiAuthority.FullSessionControl
  )
})

async function handleOpen(): Promise<void> {
  open.value = true
  error.value = null
  resumeSessionId.value = null
  loadingCampaigns.value = true
  try {
    await campaignStore.fetchCampaigns()
    if (!selectedCampaignId.value && campaigns.value.length === 1) {
      selectedCampaignId.value = campaigns.value[0].id
    }
  } catch {
    error.value = 'Could not load your campaigns.'
  } finally {
    loadingCampaigns.value = false
  }
}

async function handleStart(): Promise<void> {
  if (!selectedCampaignId.value || starting.value) return
  starting.value = true
  error.value = null
  resumeSessionId.value = null
  try {
    const runId = await sessionStore.startSession(props.adventureId, selectedCampaignId.value)
    await router.push({ name: 'adventure-solo', params: { sessionId: runId } })
  } catch (err) {
    error.value = sessionStore.error ?? 'Could not start the adventure.'
    // 409: the campaign is already running another adventure. Offer to jump back into it.
    if ((err as { response?: { status?: number } })?.response?.status === 409) {
      try {
        const active = await getActiveAdventureSession(selectedCampaignId.value)
        resumeSessionId.value = active?.id ?? null
      } catch {
        resumeSessionId.value = null
      }
    }
  } finally {
    starting.value = false
  }
}

async function handleResume(): Promise<void> {
  if (resumeSessionId.value) {
    await router.push({ name: 'adventure-solo', params: { sessionId: resumeSessionId.value } })
  }
}
</script>

<template>
  <div class="inline-block text-left" data-testid="play-solo-launcher">
    <button
      v-if="!open"
      type="button"
      data-testid="play-solo-button"
      class="rounded-md bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
      @click="handleOpen"
    >
      ▶ Play Solo
    </button>

    <div
      v-else
      class="w-80 space-y-3 rounded-lg border border-surface-700/50 bg-surface-850 p-4"
      data-testid="play-solo-panel"
    >
      <p class="text-sm font-medium text-gray-200">Play this adventure in which campaign?</p>

      <p v-if="loadingCampaigns" class="text-sm text-gray-400">Loading campaigns…</p>

      <!-- No campaign yet: send the user to create one -->
      <div v-else-if="!campaigns.length" data-testid="no-campaigns" class="space-y-2 text-sm text-gray-300">
        <p>You need a campaign to play in. Create one first, then come back.</p>
        <router-link
          to="/campaigns"
          class="inline-block rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-gray-100 hover:bg-gray-700"
        >
          Create a campaign
        </router-link>
      </div>

      <template v-else>
        <label for="solo-campaign" class="sr-only">Campaign</label>
        <select
          id="solo-campaign"
          v-model="selectedCampaignId"
          data-testid="campaign-select"
          class="w-full rounded-md border border-surface-700/50 bg-surface-900 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        >
          <option value="" disabled>Select a campaign…</option>
          <option v-for="campaign in campaigns" :key="campaign.id" :value="campaign.id">
            {{ campaign.name }}
          </option>
        </select>

        <p v-if="selectedCampaign && !aiCanAdvance" class="text-xs text-amber-300" data-testid="authority-hint">
          With this campaign's AI settings the AI DM can narrate but can't move the story along itself.
          Set the AI role to Full DM and authority to Full Session Control for hands-off play.
        </p>
      </template>

      <p v-if="error" role="alert" class="text-sm text-red-300" data-testid="launcher-error">{{ error }}</p>
      <button
        v-if="resumeSessionId"
        type="button"
        data-testid="resume-button"
        class="text-sm text-aircane-300 underline hover:text-white"
        @click="handleResume"
      >
        Resume the adventure in progress
      </button>

      <div class="flex items-center gap-2">
        <button
          type="button"
          data-testid="start-button"
          class="rounded-md bg-aircane-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!selectedCampaignId || starting"
          @click="handleStart"
        >
          {{ starting ? 'Starting…' : 'Start' }}
        </button>
        <button
          type="button"
          class="text-sm text-gray-400 hover:text-white"
          :disabled="starting"
          @click="open = false"
        >
          Cancel
        </button>
      </div>
    </div>
  </div>
</template>

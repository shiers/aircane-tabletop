<script setup lang="ts">
/**
 * StartGroupLauncher - the "Start Group Session" button for an approved group adventure.
 *
 * A group run needs a campaign and a play session that players can join. Clicking the button opens a small panel:
 *   1. (adventure not in a campaign yet) a campaign picker, like Play Solo's;
 *   2. how players get in: "Require host approval" (on by default; off lets anyone with the invite code straight
 *      in), sent to the server as the session's setting;
 *   3. how the turn moves on: only when the acting player or the host ends it (the default), or also by itself
 *      after each answered action. Carried to the lobby, which sends it when the adventure starts.
 * "Create lobby" then checks the campaign has no adventure running already, creates the play session
 * (POST /api/campaigns/{id}/start-session, local network), keeps the invite code (the server returns it only
 * once), and opens the host lobby, where players are approved and the adventure is started.
 *
 * The lobby starts the adventure in the play session made here (it sends that session's id), so an older open
 * session of the same campaign can never be picked up by mistake.
 *
 * Nothing is ended or deleted automatically. The frontend has no way to list a campaign's other
 * open play sessions, so it can only warn about a run that is already in progress.
 */
import { ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { useCampaignStore } from '@/features/campaigns/store'
import { startSession, SessionAccessMode } from '@/features/sessions/api'
import { saveInviteCode } from '@/features/sessions/inviteCodeStorage'
import { getActiveAdventureSession, getAdventureSession, type TurnAdvanceChoice } from '../api'
import { mapGroupError } from '../groupErrors'
import { openRun } from '../openRun'

const props = defineProps<{
  adventureId: string
  /** Used to name the play session. */
  adventureTitle?: string
  /** The campaign the adventure belongs to. When set there is nothing to pick. */
  campaignId?: string | null
}>()

const router = useRouter()
const campaignStore = useCampaignStore()

const open = ref(false)
const loadingCampaigns = ref(false)
const campaignsLoadFailed = ref(false)
const selectedCampaignId = ref('')
const starting = ref(false)
const error = ref<string | null>(null)
/** The campaign already has a run in progress: where to go to take it up instead. */
const runInProgress = ref<{ runId: string; sameAdventure: boolean } | null>(null)

/** Each joiner waits for the host's approval (the default), or anyone with the invite code is let straight in. */
const requireHostApproval = ref(true)
/** How the turn moves on once the adventure starts. */
const turnAdvance = ref<TurnAdvanceChoice>('ManualOnly')

const campaigns = computed(() => campaignStore.campaigns)
const needsPicker = computed(() => !props.campaignId)
const chosenCampaignId = computed(() => props.campaignId ?? selectedCampaignId.value)

function sessionName(): string {
  const title = props.adventureTitle?.trim()
  return (title ? `Group: ${title}` : 'Group session').slice(0, 200)
}

async function loadCampaigns(): Promise<void> {
  loadingCampaigns.value = true
  campaignsLoadFailed.value = false
  try {
    await campaignStore.fetchCampaigns()
    if (!selectedCampaignId.value && campaigns.value.length === 1) {
      selectedCampaignId.value = campaigns.value[0].id
    }
  } catch {
    campaignsLoadFailed.value = true
  } finally {
    loadingCampaigns.value = false
  }
}

async function handleOpen(): Promise<void> {
  error.value = null
  runInProgress.value = null
  open.value = true
  if (needsPicker.value) await loadCampaigns()
}

async function begin(campaignId: string): Promise<void> {
  if (!campaignId || starting.value) return
  starting.value = true
  error.value = null
  runInProgress.value = null
  try {
    // A campaign runs one adventure at a time. Say so now, before making a play session that
    // could only be abandoned.
    const active = await getActiveAdventureSession(campaignId)
    if (active) {
      const run = await getAdventureSession(active.id)
      const sameAdventure = run.generatedAdventureId === props.adventureId
      runInProgress.value = { runId: active.id, sameAdventure }
      error.value = sameAdventure
        ? 'This campaign is already running this adventure.'
        : `This campaign is already running "${run.adventureTitle}". Finish or abandon it before starting another adventure.`
      return
    }

    const session = await startSession(campaignId, {
      name: sessionName(),
      accessMode: SessionAccessMode.LocalLan,
      // The server applies this: with it off, joiners are approved as they join.
      requireHostApproval: requireHostApproval.value,
    })
    // The server returns the invite code only here. Keep it so a reload does not lose it.
    if (session.inviteCode) saveInviteCode(session.id, session.inviteCode)

    await router.push({
      name: 'host-session',
      params: { sessionId: session.id },
      query: {
        adventure: props.adventureId,
        ...(turnAdvance.value === 'AfterEachAction' ? { turnAdvance: 'AfterEachAction' } : {}),
      },
    })
  } catch (err) {
    error.value = mapGroupError(err, 'create-session').message
  } finally {
    starting.value = false
  }
}

async function handleStart(): Promise<void> {
  await begin(chosenCampaignId.value)
}

async function handleOpenRun(): Promise<void> {
  if (runInProgress.value) await openRun(router, runInProgress.value.runId)
}
</script>

<template>
  <div class="inline-block max-w-full text-left" data-testid="start-group-launcher">
    <button
      v-if="!open"
      type="button"
      data-testid="start-group-button"
      class="rounded-md bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
      :disabled="starting"
      @click="handleOpen"
    >
      {{ starting ? 'Starting…' : '👥 Start Group Session' }}
    </button>

    <div
      v-else
      class="w-full max-w-xs space-y-3 rounded-lg border border-surface-700/50 bg-surface-850 p-4 sm:w-80"
      data-testid="start-group-panel"
    >
      <template v-if="needsPicker">
        <p class="text-sm font-medium text-gray-200">Run this group adventure in which campaign?</p>

        <p v-if="loadingCampaigns" class="text-sm text-gray-400">Loading campaigns…</p>

        <!-- The list could not be loaded: not the same as having no campaigns -->
        <div v-else-if="campaignsLoadFailed" data-testid="campaigns-load-failed" role="alert" class="space-y-2 text-sm text-red-300">
          <p>Could not load your campaigns.</p>
          <button
            type="button"
            data-testid="retry-campaigns"
            class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-gray-100 hover:bg-gray-700"
            @click="loadCampaigns"
          >
            Try again
          </button>
        </div>

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
          <label for="group-campaign" class="sr-only">Campaign</label>
          <select
            id="group-campaign"
            v-model="selectedCampaignId"
            data-testid="campaign-select"
            class="w-full rounded-md border border-surface-700/50 bg-surface-900 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          >
            <option value="" disabled>Select a campaign…</option>
            <option v-for="campaign in campaigns" :key="campaign.id" :value="campaign.id">
              {{ campaign.name }}
            </option>
          </select>
        </template>
      </template>

      <!-- How players get in, and how the turn moves on -->
      <label class="flex items-start gap-2 text-sm text-gray-200">
        <input v-model="requireHostApproval" type="checkbox" class="mt-0.5" data-testid="require-approval" />
        <span>
          Require host approval
          <span class="block text-xs text-gray-400">
            {{ requireHostApproval ? 'You let each player in.' : 'Anyone with the invite code joins straight away.' }}
          </span>
        </span>
      </label>

      <div>
        <label for="group-turn-advance" class="block text-sm text-gray-200">The turn moves on</label>
        <select
          id="group-turn-advance"
          v-model="turnAdvance"
          data-testid="turn-advance-select"
          class="mt-1 w-full rounded-md border border-surface-700/50 bg-surface-900 px-3 py-2 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        >
          <option value="ManualOnly">When the player or I end it</option>
          <option value="AfterEachAction">After each action</option>
        </select>
      </div>

      <p class="text-xs text-gray-400">
        This opens a lobby where players join with an invite code and you choose the turn order.
      </p>

      <p v-if="error" role="alert" class="text-sm text-red-300" data-testid="launcher-error">{{ error }}</p>
      <button
        v-if="runInProgress"
        type="button"
        data-testid="open-run-button"
        class="text-sm text-aircane-300 underline hover:text-white"
        @click="handleOpenRun"
      >
        {{ runInProgress.sameAdventure ? 'Open the adventure in progress' : 'Open the adventure that is running' }}
      </button>

      <div class="flex items-center gap-2">
        <button
          type="button"
          data-testid="start-button"
          class="rounded-md bg-aircane-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          :disabled="!chosenCampaignId || starting"
          @click="handleStart"
        >
          {{ starting ? 'Starting…' : 'Create lobby' }}
        </button>
        <button type="button" class="text-sm text-gray-400 hover:text-white" :disabled="starting" @click="open = false">
          Cancel
        </button>
      </div>
    </div>
  </div>
</template>

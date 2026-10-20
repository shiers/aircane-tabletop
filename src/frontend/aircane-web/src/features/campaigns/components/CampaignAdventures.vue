<script setup lang="ts">
/**
 * CampaignAdventures - the "Adventures" section of a campaign's page: the adventures generated for
 * this campaign, each with the actions that make sense for its status.
 *
 *   Draft              Review · Approve · Remove from campaign · Delete adventure
 *   Approved / Active  Review · Play Solo (solo adventures) or Start Group Session (group adventures) ·
 *                      Remove from campaign · Delete adventure
 *   Rejected           Review · Remove from campaign · Delete adventure
 *
 * "Remove from campaign" only detaches: the adventure stays in the Adventure Forge. "Delete adventure" erases it
 * (and its runs) for good, after naming exactly what goes. Both wait while a run is in progress. "Attach adventure"
 * opens a picker for putting an existing adventure in this campaign.
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  listCampaignAdventures,
  approveAdventure,
  deleteAdventure,
  getAdventureDeleteImpact,
  unassignAdventure,
  isGroupAdventure,
  type CampaignAdventure,
} from '@/features/adventure-generation/api'
import { describeDelete, messageOf } from '@/features/adventure-generation/adventureActions'
import AttachAdventurePicker from './AttachAdventurePicker.vue'
import AdventureCard from '@/features/adventure-generation/components/AdventureCard.vue'
import StartGroupLauncher from '@/features/adventures/components/StartGroupLauncher.vue'
import { openRun } from '@/features/adventures/openRun'
import { useAdventureGenerationStore } from '@/features/adventure-generation/store'
import { useAdventureSessionStore } from '@/features/adventures/stores/adventureSessionStore'
import { getActiveAdventureSession } from '@/features/adventures/api'

const props = defineProps<{
  campaignId: string
  /** Named in the confirmations when an adventure is attached. */
  campaignName?: string | null
}>()

const router = useRouter()
const generation = useAdventureGenerationStore()
const sessionStore = useAdventureSessionStore()

const adventures = ref<CampaignAdventure[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
/** Adventure currently being approved / deleted / started, so its buttons can be disabled. */
const busyId = ref<string | null>(null)
/** Set when Play Solo hit "this campaign already has an adventure in progress". */
const resumeSessionId = ref<string | null>(null)
/** The "Attach adventure" picker is open. */
const attaching = ref(false)

/** The list could not be loaded: the page says so, rather than claiming there are no adventures. */
const loadFailed = ref(false)

const isEmpty = computed(() => !loading.value && !loadFailed.value && adventures.value.length === 0)

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  loadFailed.value = false
  try {
    adventures.value = await listCampaignAdventures(props.campaignId)
  } catch (e) {
    error.value = messageOf(e, 'Could not load adventures.')
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(load)

// A generation that finishes while this page is open should show up without a refresh.
watch(
  () => generation.status,
  (status) => {
    if (status === 'completed') void load()
  },
)

const canPlay = (a: CampaignAdventure) => a.status === 'Approved' || a.status === 'Active'

function review(a: CampaignAdventure): void {
  void router.push({ name: 'adventure-review', params: { adventureId: a.id } })
}

function generate(): void {
  void router.push({ name: 'adventure-generate', query: { campaignId: props.campaignId } })
}

async function approve(a: CampaignAdventure): Promise<void> {
  busyId.value = a.id
  error.value = null
  try {
    await approveAdventure(a.id)
    a.status = 'Approved' // in place: no reload, the card just changes its actions
  } catch (e) {
    error.value = messageOf(e, 'Could not approve the adventure.')
  } finally {
    busyId.value = null
  }
}

/** A run in progress holds the adventure: it can be neither detached nor deleted until the run is done. */
const runHolds = (a: CampaignAdventure) => !!a.activeSession
const HELD_REASON = 'A run of this adventure is in progress. Finish or abandon it first.'

/** Take the adventure out of this campaign. It stays in the Forge, with its runs and history. */
async function removeFromCampaign(a: CampaignAdventure): Promise<void> {
  if (!confirm(`Remove "${a.title}" from this campaign?\n\nThe adventure stays in the Adventure Forge, and you can attach it again later.`)) return
  busyId.value = a.id
  error.value = null
  try {
    await unassignAdventure(a.id)
    adventures.value = adventures.value.filter((x) => x.id !== a.id)
  } catch (e) {
    error.value = messageOf(e, 'Could not remove the adventure from the campaign.')
  } finally {
    busyId.value = null
  }
}

/** Erase the adventure for good, after saying exactly what goes. */
async function remove(a: CampaignAdventure): Promise<void> {
  busyId.value = a.id
  error.value = null
  try {
    const impact = await getAdventureDeleteImpact(a.id)
    if (impact.inProgressRuns > 0) {
      error.value = `"${a.title}" has a run in progress, so it cannot be deleted. Finish or abandon the run first.`
      return
    }
    if (!confirm(describeDelete(impact))) return
    await deleteAdventure(a.id)
    adventures.value = adventures.value.filter((x) => x.id !== a.id)
  } catch (e) {
    error.value = messageOf(e, 'Could not delete the adventure.')
  } finally {
    busyId.value = null
  }
}

function attached(a: CampaignAdventure): void {
  attaching.value = false
  adventures.value = [a, ...adventures.value.filter((x) => x.id !== a.id)]
}

async function playSolo(a: CampaignAdventure): Promise<void> {
  busyId.value = a.id
  error.value = null
  resumeSessionId.value = null
  try {
    const runId = await sessionStore.startSession(a.id, props.campaignId)
    await router.push({ name: 'adventure-solo', params: { sessionId: runId } })
  } catch (e) {
    error.value = sessionStore.error ?? messageOf(e, 'Could not start the adventure.')
    if ((e as { response?: { status?: number } })?.response?.status === 409) {
      try {
        resumeSessionId.value = (await getActiveAdventureSession(props.campaignId))?.id ?? null
      } catch {
        resumeSessionId.value = null
      }
    }
  } finally {
    busyId.value = null
  }
}

/** Go back into a run that is already in progress (no new run is started). */
async function resumeRun(runId: string): Promise<void> {
  await openRun(router, runId)
}

async function resume(): Promise<void> {
  if (resumeSessionId.value) {
    await router.push({ name: 'adventure-solo', params: { sessionId: resumeSessionId.value } })
  }
}

</script>

<template>
  <section aria-labelledby="campaign-adventures-heading" class="space-y-4" data-testid="adventures-section">
    <div class="flex items-center justify-between gap-4 border-b border-surface-700/50 pb-2">
      <h2 id="campaign-adventures-heading" class="text-lg font-semibold text-white">Adventures</h2>
      <div class="flex flex-wrap justify-end gap-2">
        <button
          type="button"
          data-testid="attach-adventure"
          class="rounded-md border border-surface-700/50 bg-surface-850 px-3 py-1.5 text-sm font-semibold text-gray-100 hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          :aria-expanded="attaching"
          @click="attaching = !attaching"
        >
          Attach adventure
        </button>
        <button
          v-if="!isEmpty"
          type="button"
          data-testid="generate-adventure"
          class="rounded-md bg-aircane-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="generate"
        >
          + Generate Adventure
        </button>
      </div>
    </div>

    <AttachAdventurePicker
      v-if="attaching"
      :campaign-id="campaignId"
      :campaign-name="campaignName"
      @attached="attached"
      @close="attaching = false"
    />

    <div
      v-if="error"
      role="alert"
      class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
      data-testid="adventures-error"
    >
      {{ error }}
      <button
        v-if="resumeSessionId"
        type="button"
        class="ml-2 underline hover:text-white"
        data-testid="resume-button"
        @click="resume"
      >
        Resume the adventure in progress
      </button>
      <button
        v-if="loadFailed"
        type="button"
        class="ml-2 underline hover:text-white"
        data-testid="adventures-retry"
        @click="load"
      >
        Try again
      </button>
    </div>

    <div v-if="loading" class="space-y-3" aria-busy="true">
      <div v-for="n in 2" :key="n" class="h-20 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="isEmpty"
      class="rounded-lg border border-dashed border-surface-700/50 px-6 py-10 text-center"
      data-testid="adventures-empty"
    >
      <p class="text-gray-300">No adventures yet.</p>
      <button
        type="button"
        data-testid="generate-adventure"
        class="mt-3 rounded-md bg-aircane-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="generate"
      >
        + Generate Adventure
      </button>
    </div>

    <ul v-else-if="!loadFailed" class="space-y-3">
      <AdventureCard v-for="a in adventures" :key="a.id" :adventure="a">
        <button type="button" data-testid="review-button" class="adventure-action" @click="review(a)">Review</button>
        <button
          v-if="a.status === 'Draft'"
          type="button"
          data-testid="approve-button"
          class="adventure-action"
          :disabled="busyId === a.id"
          @click="approve(a)"
        >Approve</button>
        <button
          v-if="a.activeSession"
          type="button"
          data-testid="resume-adventure-button"
          class="adventure-action adventure-action--primary"
          @click="resumeRun(a.activeSession.sessionId)"
        >Resume</button>
        <StartGroupLauncher
          v-else-if="canPlay(a) && isGroupAdventure(a.mode)"
          :adventure-id="a.id"
          :adventure-title="a.title"
          :campaign-id="campaignId"
        />
        <button
          v-else-if="canPlay(a)"
          type="button"
          data-testid="play-solo-button"
          class="adventure-action adventure-action--primary"
          :disabled="busyId === a.id"
          @click="playSolo(a)"
        >Play Solo</button>
        <button
          type="button"
          data-testid="remove-from-campaign-button"
          class="adventure-action"
          :disabled="busyId === a.id || runHolds(a)"
          :title="runHolds(a) ? HELD_REASON : 'Take it out of this campaign. It stays in the Adventure Forge.'"
          @click="removeFromCampaign(a)"
        >Remove from campaign</button>
        <button
          type="button"
          data-testid="delete-button"
          class="adventure-action adventure-action--danger border-dashed"
          :disabled="busyId === a.id || runHolds(a)"
          :title="runHolds(a) ? HELD_REASON : 'Erase this adventure and its runs for good.'"
          @click="remove(a)"
        >🗑 Delete adventure</button>
        <p v-if="runHolds(a)" class="basis-full text-right text-xs text-gray-400" data-testid="run-holds-note">
          {{ HELD_REASON }}
        </p>
      </AdventureCard>
    </ul>
  </section>
</template>


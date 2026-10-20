<script setup lang="ts">
/**
 * AdventuresView - the Adventure Forge landing page. Route: /adventures
 *
 * Lists every adventure, each labelled with the campaign it is in (if any), with the actions to put it to use,
 * and a button to generate a new one. Unassigned adventures first.
 *
 *   Not in a campaign   Assign to Campaign · Approve (drafts) · Review · Play Solo (solo adventures) or
 *                       Start Group Session (group adventures) · Delete adventure
 *   In a campaign       Move to Campaign · Remove from campaign · Open campaign · Approve (drafts) · Review ·
 *                       Delete adventure (play it from its campaign)
 *
 * Attaching asks for agreement when it would take the adventure out of another campaign or the game systems
 * differ. A run in progress holds an adventure: it can be neither detached, moved nor deleted until it is done.
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  listAllAdventures,
  approveAdventure,
  deleteAdventure,
  getAdventureDeleteImpact,
  unassignAdventure,
  isGroupAdventure,
  type CampaignAdventure,
} from './api'
import { attachWithConfirmation, describeDelete, messageOf } from './adventureActions'
import { useAdventureGenerationStore } from './store'
import { useCampaignStore } from '@/features/campaigns/store'
import PlaySoloLauncher from '@/features/adventures/components/PlaySoloLauncher.vue'
import StartGroupLauncher from '@/features/adventures/components/StartGroupLauncher.vue'
import { openRun } from '@/features/adventures/openRun'
import AdventureCard from './components/AdventureCard.vue'

const router = useRouter()
const generation = useAdventureGenerationStore()
const campaignStore = useCampaignStore()

const adventures = ref<CampaignAdventure[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const busyId = ref<string | null>(null)
/** The adventure whose "Assign to Campaign" picker is open. */
const assigningId = ref<string | null>(null)
/** The campaign list could not be loaded, so the picker must not claim there are none. */
const campaignsLoadFailed = ref(false)

/** The list could not be loaded: the page says so, rather than claiming there are no adventures. */
const loadFailed = ref(false)

const isEmpty = computed(() => !loading.value && !loadFailed.value && adventures.value.length === 0)
const campaigns = computed(() => campaignStore.campaigns)
const canPlay = (a: CampaignAdventure) => a.status === 'Approved' || a.status === 'Active'

/** Unassigned adventures first (the ones waiting to be put to use), then the rest, each newest first. */
const ordered = computed(() => {
  const list = adventures.value
  return [...list.filter((a) => !a.campaignId), ...list.filter((a) => !!a.campaignId)]
})

/** A run in progress holds the adventure: it can be neither detached, moved nor deleted until it is done. */
const runHolds = (a: CampaignAdventure) => !!a.activeSession
const HELD_REASON = 'A run of this adventure is in progress. Finish or abandon it first.'

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  loadFailed.value = false
  try {
    adventures.value = await listAllAdventures()
  } catch (e) {
    error.value = messageOf(e, 'Could not load adventures.')
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(load)

// A freshly generated adventure lands here: show it as soon as it is ready.
watch(
  () => generation.status,
  (status) => {
    if (status === 'completed') void load()
  },
)

function generate(): void {
  void router.push({ name: 'adventure-generate' })
}

function resumeRun(runId: string): void {
  void openRun(router, runId)
}

function review(a: CampaignAdventure): void {
  void router.push({ name: 'adventure-review', params: { adventureId: a.id } })
}

async function openAssign(a: CampaignAdventure): Promise<void> {
  if (assigningId.value === a.id) {
    assigningId.value = null
    return
  }
  assigningId.value = a.id
  await loadCampaignsForPicker()
}

/** Loads the campaigns for the picker, remembering a failure so it can be shown (and retried). */
async function loadCampaignsForPicker(): Promise<void> {
  campaignsLoadFailed.value = false
  if (campaignStore.campaigns.length) return
  try {
    await campaignStore.fetchCampaigns()
  } catch {
    campaignsLoadFailed.value = true
  }
}

async function assign(a: CampaignAdventure, campaignId: string): Promise<void> {
  if (!campaignId) return
  busyId.value = a.id
  error.value = null
  try {
    const name = campaigns.value.find((c) => c.id === campaignId)?.name ?? 'this campaign'
    const updated = await attachWithConfirmation(a, campaignId, name)
    if (updated) {
      // In place: the card now says which campaign it is in.
      a.campaignId = updated.campaignId ?? campaignId
      a.campaignName = updated.campaignName ?? name
      assigningId.value = null
    }
  } catch (e) {
    error.value = messageOf(e, 'Could not assign the adventure.')
  } finally {
    busyId.value = null
  }
}

/** Take the adventure out of its campaign. It stays here, with its runs and history. */
async function removeFromCampaign(a: CampaignAdventure): Promise<void> {
  if (!confirm(`Remove "${a.title}" from the campaign "${a.campaignName ?? 'its campaign'}"?\n\nThe adventure stays in the Adventure Forge, and you can attach it again later.`)) return
  busyId.value = a.id
  error.value = null
  try {
    await unassignAdventure(a.id)
    a.campaignId = null
    a.campaignName = null
  } catch (e) {
    error.value = messageOf(e, 'Could not remove the adventure from the campaign.')
  } finally {
    busyId.value = null
  }
}

async function approve(a: CampaignAdventure): Promise<void> {
  busyId.value = a.id
  error.value = null
  try {
    await approveAdventure(a.id)
    a.status = 'Approved' // in place: the card just changes its actions
  } catch (e) {
    error.value = messageOf(e, 'Could not approve the adventure.')
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
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6" data-testid="adventures-view">
    <div class="flex items-center justify-between gap-4">
      <h1 class="text-2xl font-bold text-white">Adventure Forge</h1>
      <button
        type="button"
        data-testid="generate-new"
        class="rounded-md bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="generate"
      >
        + Generate New Adventure
      </button>
    </div>

    <section aria-labelledby="adventures-heading" class="space-y-4">
      <h2 id="adventures-heading" class="border-b border-surface-700/50 pb-2 text-lg font-semibold text-white">
        Your Adventures
      </h2>

      <div
        v-if="error"
        role="alert"
        class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
        data-testid="adventures-error"
      >
        {{ error }}
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

      <p v-else-if="isEmpty" class="rounded-lg border border-dashed border-surface-700/50 px-6 py-10 text-center text-gray-300" data-testid="unassigned-empty">
        No adventures yet.
      </p>

      <ul v-else-if="!loadFailed" class="space-y-3">
        <AdventureCard v-for="a in ordered" :key="a.id" :adventure="a">
          <div class="relative">
            <button
              type="button"
              data-testid="assign-button"
              class="adventure-action"
              :disabled="busyId === a.id || runHolds(a)"
              :title="runHolds(a) ? HELD_REASON : undefined"
              @click="openAssign(a)"
            >{{ a.campaignId ? 'Move to Campaign' : 'Assign to Campaign' }}</button>
            <div
              v-if="assigningId === a.id"
              class="absolute right-0 z-10 mt-1 w-56 rounded-md border border-surface-700/50 bg-surface-850 p-2 shadow-lg"
              data-testid="assign-picker"
            >
              <div v-if="campaignsLoadFailed" class="space-y-1 text-sm text-red-300" role="alert" data-testid="assign-load-failed">
                <p>Could not load your campaigns.</p>
                <button type="button" data-testid="assign-retry" class="text-aircane-300 underline" @click="loadCampaignsForPicker">
                  Try again
                </button>
              </div>
              <p v-else-if="!campaigns.length" class="text-sm text-gray-300" data-testid="assign-no-campaigns">
                You have no campaigns yet.
                <router-link to="/campaigns" class="text-aircane-300 underline">Create one</router-link>
              </p>
              <select
                v-else
                data-testid="assign-select"
                aria-label="Campaign"
                class="w-full rounded-md border border-surface-700/50 bg-surface-900 px-2 py-1 text-sm text-gray-100"
                @change="assign(a, ($event.target as HTMLSelectElement).value)"
              >
                <option value="" selected disabled>Choose a campaign…</option>
                <option v-for="c in campaigns.filter((x) => x.id !== a.campaignId)" :key="c.id" :value="c.id">{{ c.name }}</option>
              </select>
            </div>
          </div>
          <button
            v-if="a.status === 'Draft'"
            type="button"
            data-testid="approve-button"
            class="adventure-action"
            :disabled="busyId === a.id"
            @click="approve(a)"
          >Approve</button>
          <button type="button" data-testid="review-button" class="adventure-action" @click="review(a)">Review</button>
          <button
            v-if="a.campaignId"
            type="button"
            data-testid="remove-from-campaign-button"
            class="adventure-action"
            :disabled="busyId === a.id || runHolds(a)"
            :title="runHolds(a) ? HELD_REASON : 'Take it out of its campaign. It stays here.'"
            @click="removeFromCampaign(a)"
          >Remove from campaign</button>
          <router-link
            v-if="a.campaignId"
            :to="{ name: 'campaign-detail', params: { campaignId: a.campaignId } }"
            class="adventure-action"
            data-testid="open-campaign-link"
          >Open campaign</router-link>
          <button
            v-if="a.activeSession"
            type="button"
            data-testid="resume-adventure-button"
            class="adventure-action adventure-action--primary"
            @click="resumeRun(a.activeSession.sessionId)"
          >Resume</button>
          <!-- An adventure in a campaign is played from that campaign's page -->
          <StartGroupLauncher
            v-else-if="!a.campaignId && canPlay(a) && isGroupAdventure(a.mode)"
            :adventure-id="a.id"
            :adventure-title="a.title"
          />
          <PlaySoloLauncher v-else-if="!a.campaignId && canPlay(a)" :adventure-id="a.id" />
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
  </div>
</template>

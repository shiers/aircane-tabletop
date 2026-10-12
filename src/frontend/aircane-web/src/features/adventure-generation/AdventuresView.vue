<script setup lang="ts">
/**
 * AdventuresView - the Adventure Forge landing page. Route: /adventures
 *
 * Lists the adventures that belong to no campaign yet (generated without one, or before campaigns
 * were linked), with the actions to put them to use, and a button to generate a new one.
 *
 *   Draft     Assign to Campaign · Approve · Review · Delete
 *   Approved  Assign to Campaign · Review · Play Solo · Delete
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  listUnassignedAdventures,
  assignAdventureToCampaign,
  approveAdventure,
  deleteAdventure,
  type CampaignAdventure,
} from './api'
import { useAdventureGenerationStore } from './store'
import { useCampaignStore } from '@/features/campaigns/store'
import PlaySoloLauncher from '@/features/adventures/components/PlaySoloLauncher.vue'
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

const isEmpty = computed(() => !loading.value && adventures.value.length === 0)
const campaigns = computed(() => campaignStore.campaigns)
const canPlay = (a: CampaignAdventure) => a.status === 'Approved' || a.status === 'Active'

function messageOf(e: unknown, fallback: string): string {
  const detail = (e as { response?: { data?: { detail?: string } } })?.response?.data?.detail
  return detail ?? (e instanceof Error ? e.message : fallback)
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    adventures.value = await listUnassignedAdventures()
  } catch (e) {
    error.value = messageOf(e, 'Could not load adventures.')
  } finally {
    loading.value = false
  }
}

onMounted(load)

// An adventure generated without a campaign lands here: show it as soon as it is ready.
watch(
  () => generation.status,
  (status) => {
    if (status === 'completed') void load()
  },
)

function generate(): void {
  void router.push({ name: 'adventure-generate' })
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
  if (!campaignStore.campaigns.length) {
    try {
      await campaignStore.fetchCampaigns()
    } catch {
      error.value = 'Could not load your campaigns.'
    }
  }
}

async function assign(a: CampaignAdventure, campaignId: string): Promise<void> {
  if (!campaignId) return
  busyId.value = a.id
  error.value = null
  try {
    await assignAdventureToCampaign(a.id, campaignId)
    // It now belongs to the campaign, so it leaves this list and shows up on the campaign's page.
    adventures.value = adventures.value.filter((x) => x.id !== a.id)
    assigningId.value = null
  } catch (e) {
    error.value = messageOf(e, 'Could not assign the adventure.')
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

async function remove(a: CampaignAdventure): Promise<void> {
  if (!confirm(`Delete "${a.title}"? This cannot be undone.`)) return
  busyId.value = a.id
  error.value = null
  try {
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

    <section aria-labelledby="unassigned-heading" class="space-y-4">
      <h2 id="unassigned-heading" class="border-b border-surface-700/50 pb-2 text-lg font-semibold text-white">
        Unassigned Adventures
      </h2>

      <div
        v-if="error"
        role="alert"
        class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
        data-testid="adventures-error"
      >
        {{ error }}
      </div>

      <div v-if="loading" class="space-y-3" aria-busy="true">
        <div v-for="n in 2" :key="n" class="h-20 animate-pulse rounded-lg bg-gray-800" />
      </div>

      <p v-else-if="isEmpty" class="rounded-lg border border-dashed border-surface-700/50 px-6 py-10 text-center text-gray-300" data-testid="unassigned-empty">
        No unassigned adventures.
      </p>

      <ul v-else class="space-y-3">
        <AdventureCard v-for="a in adventures" :key="a.id" :adventure="a">
          <div class="relative">
            <button
              type="button"
              data-testid="assign-button"
              class="adventure-action"
              :disabled="busyId === a.id"
              @click="openAssign(a)"
            >Assign to Campaign</button>
            <div
              v-if="assigningId === a.id"
              class="absolute right-0 z-10 mt-1 w-56 rounded-md border border-surface-700/50 bg-surface-850 p-2 shadow-lg"
              data-testid="assign-picker"
            >
              <p v-if="!campaigns.length" class="text-sm text-gray-300" data-testid="assign-no-campaigns">
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
                <option v-for="c in campaigns" :key="c.id" :value="c.id">{{ c.name }}</option>
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
          <PlaySoloLauncher v-if="canPlay(a)" :adventure-id="a.id" />
          <button
            type="button"
            data-testid="delete-button"
            class="adventure-action adventure-action--danger"
            :disabled="busyId === a.id"
            @click="remove(a)"
          >Delete</button>
        </AdventureCard>
      </ul>
    </section>
  </div>
</template>

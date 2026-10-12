<script setup lang="ts">
/**
 * CampaignAdventures - the "Adventures" section of a campaign's page: the adventures generated for
 * this campaign, each with the actions that make sense for its status.
 *
 *   Draft              Review · Approve · Delete
 *   Approved / Active  Review · Play Solo · Delete
 *   Rejected           Review · Delete
 */
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  listCampaignAdventures,
  approveAdventure,
  deleteAdventure,
  type CampaignAdventure,
} from '@/features/adventure-generation/api'
import { useAdventureGenerationStore } from '@/features/adventure-generation/store'
import { useAdventureSessionStore } from '@/features/adventures/stores/adventureSessionStore'
import { getActiveAdventureSession } from '@/features/adventures/api'

const props = defineProps<{
  campaignId: string
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

const isEmpty = computed(() => !loading.value && adventures.value.length === 0)

function messageOf(e: unknown, fallback: string): string {
  const detail = (e as { response?: { data?: { detail?: string } } })?.response?.data?.detail
  return detail ?? (e instanceof Error ? e.message : fallback)
}

async function load(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    adventures.value = await listCampaignAdventures(props.campaignId)
  } catch (e) {
    error.value = messageOf(e, 'Could not load adventures.')
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

function metaLine(a: CampaignAdventure): string {
  const length = a.length ? a.length.charAt(0).toUpperCase() + a.length.slice(1) : null
  return [a.gameSystem, a.mode, length, a.averageLevel != null ? `Level ${a.averageLevel}` : null]
    .filter(Boolean)
    .join(' · ')
}

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

async function resume(): Promise<void> {
  if (resumeSessionId.value) {
    await router.push({ name: 'adventure-solo', params: { sessionId: resumeSessionId.value } })
  }
}

const statusClass = (status: string) =>
  ({
    Approved: 'border-green-700 bg-green-900/50 text-green-300',
    Active: 'border-aircane-700 bg-aircane-900/50 text-aircane-300',
    Rejected: 'border-red-700 bg-red-900/50 text-red-300',
  })[status] ?? 'border-yellow-700 bg-yellow-900/50 text-yellow-300'
</script>

<template>
  <section aria-labelledby="campaign-adventures-heading" class="space-y-4" data-testid="adventures-section">
    <div class="flex items-center justify-between gap-4 border-b border-surface-700/50 pb-2">
      <h2 id="campaign-adventures-heading" class="text-lg font-semibold text-white">Adventures</h2>
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

    <ul v-else class="space-y-3">
      <li
        v-for="a in adventures"
        :key="a.id"
        class="rounded-lg border border-surface-700/50 bg-surface-850 p-4"
        data-testid="adventure-card"
        :data-status="a.status"
      >
        <div class="flex items-start justify-between gap-3">
          <h3 class="min-w-0 truncate font-medium text-gray-100" data-testid="adventure-title">✦ {{ a.title }}</h3>
          <span
            class="shrink-0 rounded-full border px-2 py-0.5 text-xs"
            :class="statusClass(a.status)"
            data-testid="adventure-status"
          >{{ a.status }}</span>
        </div>
        <p class="mt-1 text-sm text-gray-400" data-testid="adventure-meta">{{ metaLine(a) }}</p>

        <div class="mt-3 flex flex-wrap justify-end gap-2">
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
            v-if="canPlay(a)"
            type="button"
            data-testid="play-solo-button"
            class="adventure-action adventure-action--primary"
            :disabled="busyId === a.id"
            @click="playSolo(a)"
          >Play Solo</button>
          <button
            type="button"
            data-testid="delete-button"
            class="adventure-action adventure-action--danger"
            :disabled="busyId === a.id"
            @click="remove(a)"
          >Delete</button>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.adventure-action {
  border-radius: 0.375rem;
  border: 1px solid rgb(55 65 81);
  background: rgb(31 41 55);
  padding: 0.25rem 0.75rem;
  font-size: 0.875rem;
  color: rgb(243 244 246);
}
.adventure-action:hover:not(:disabled) {
  background: rgb(55 65 81);
}
.adventure-action:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
.adventure-action--primary {
  border-color: transparent;
  background: var(--color-purple, #7c3aed);
  font-weight: 600;
}
.adventure-action--danger {
  border-color: rgb(153 27 27);
  background: rgb(69 10 10);
  color: rgb(252 165 165);
}
</style>

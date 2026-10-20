<script setup lang="ts">
/**
 * AttachAdventurePicker - lists the adventures that can be put in this campaign and attaches the one the host
 * picks. Adventures that belong to no campaign come first; a toggle adds the ones that are in other campaigns
 * (labelled with where they are now). Anything the server wants agreed to first (leaving another campaign, a
 * different game system) is put to the host by name before the request is sent again.
 */
import { ref, computed, onMounted } from 'vue'
import {
  listAllAdventures,
  isGroupAdventure,
  type CampaignAdventure,
} from '@/features/adventure-generation/api'
import { attachWithConfirmation, messageOf } from '@/features/adventure-generation/adventureActions'

const props = defineProps<{
  campaignId: string
  /** For the confirmations; the page knows it once the campaign has loaded. */
  campaignName?: string | null
}>()

const emit = defineEmits<{
  (e: 'attached', adventure: CampaignAdventure): void
  (e: 'close'): void
}>()

const all = ref<CampaignAdventure[]>([])
const loading = ref(true)
/** The server's message when the list could not be loaded: never shown as "nothing to attach". */
const loadError = ref<string | null>(null)
const attachError = ref<string | null>(null)
const attachingId = ref<string | null>(null)
const showOthers = ref(false)

/** Everything that is not in this campaign already. */
const candidates = computed(() => all.value.filter((a) => a.campaignId !== props.campaignId))
const unassigned = computed(() => candidates.value.filter((a) => !a.campaignId))
const inOthers = computed(() => candidates.value.filter((a) => !!a.campaignId))
/** Unassigned first, then (when asked for) the ones in other campaigns. */
const listed = computed(() => (showOthers.value ? [...unassigned.value, ...inOthers.value] : unassigned.value))

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    all.value = await listAllAdventures()
  } catch (e) {
    loadError.value = messageOf(e, 'Could not load adventures.')
  } finally {
    loading.value = false
  }
}

onMounted(load)

function meta(a: CampaignAdventure): string {
  const mode = a.mode ? (isGroupAdventure(a.mode) ? 'Group' : 'Solo') : null
  return [mode, a.averageLevel != null ? `Level ${a.averageLevel}` : null, a.gameSystem].filter(Boolean).join(' · ')
}

async function attach(a: CampaignAdventure): Promise<void> {
  attachingId.value = a.id
  attachError.value = null
  try {
    const updated = await attachWithConfirmation(a, props.campaignId, props.campaignName ?? 'this campaign')
    if (updated) emit('attached', { ...a, ...updated })
  } catch (e) {
    attachError.value = messageOf(e, 'Could not attach the adventure.')
  } finally {
    attachingId.value = null
  }
}
</script>

<template>
  <div
    class="space-y-3 rounded-lg border border-surface-700/50 bg-surface-900/60 p-4"
    role="region"
    aria-label="Attach an adventure"
    data-testid="attach-picker"
  >
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 class="font-medium text-gray-100">Attach an adventure</h3>
      <button
        type="button"
        class="text-sm text-gray-400 hover:text-white"
        data-testid="attach-close"
        @click="emit('close')"
      >
        Close
      </button>
    </div>

    <label class="flex items-start gap-2 text-sm text-gray-300">
      <input v-model="showOthers" type="checkbox" class="mt-0.5" data-testid="attach-show-assigned" />
      <span>Also show adventures that are in other campaigns</span>
    </label>

    <div v-if="attachError" role="alert" class="rounded-md border border-red-800 bg-red-950 px-3 py-2 text-sm text-red-300" data-testid="attach-error">
      {{ attachError }}
    </div>

    <div v-if="loading" class="space-y-2" aria-busy="true">
      <div v-for="n in 2" :key="n" class="h-14 animate-pulse rounded-md bg-gray-800" />
    </div>

    <!-- A failed load is not "nothing to attach" -->
    <div v-else-if="loadError" role="alert" class="space-y-2 text-sm text-red-300" data-testid="attach-load-failed">
      <p>{{ loadError }}</p>
      <button
        type="button"
        class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1 text-gray-100 hover:bg-gray-700"
        data-testid="attach-retry"
        @click="load"
      >
        Try again
      </button>
    </div>

    <div v-else-if="listed.length === 0" class="text-sm text-gray-300" data-testid="attach-empty">
      <p v-if="!showOthers && inOthers.length > 0">
        No unassigned adventures. {{ inOthers.length }}
        {{ inOthers.length === 1 ? 'adventure is' : 'adventures are' }} in other campaigns: turn on the option above
        to see {{ inOthers.length === 1 ? 'it' : 'them' }}.
      </p>
      <p v-else>
        There is nothing to attach.
        <router-link to="/adventures" class="text-aircane-300 underline" data-testid="attach-forge-link">
          Open the Adventure Forge
        </router-link>
        to generate one.
      </p>
    </div>

    <ul v-else class="space-y-2">
      <li
        v-for="a in listed"
        :key="a.id"
        class="flex flex-col gap-2 rounded-md border border-surface-700/50 bg-surface-850 p-3 sm:flex-row sm:items-center sm:justify-between"
        data-testid="attach-option"
      >
        <div class="min-w-0">
          <p class="flex flex-wrap items-center gap-2 font-medium text-gray-100">
            <span class="min-w-0 break-words" data-testid="attach-option-title">{{ a.title }}</span>
            <span
              class="shrink-0 rounded-full border px-2 py-0.5 text-xs"
              :class="a.status === 'Draft' ? 'border-yellow-700 bg-yellow-900/50 text-yellow-300' : 'border-gray-600 bg-gray-800 text-gray-300'"
              data-testid="attach-option-status"
            >{{ a.status }}</span>
          </p>
          <p class="mt-0.5 break-words text-sm text-gray-400" data-testid="attach-option-meta">{{ meta(a) }}</p>
          <p v-if="a.campaignId" class="mt-0.5 text-sm text-amber-300" data-testid="attach-option-campaign">
            currently in {{ a.campaignName ?? 'another campaign' }}
          </p>
        </div>
        <button
          type="button"
          class="adventure-action adventure-action--primary shrink-0 self-start sm:self-center"
          data-testid="attach-option-button"
          :disabled="attachingId !== null"
          @click="attach(a)"
        >
          {{ attachingId === a.id ? 'Attaching…' : 'Attach' }}
        </button>
      </li>
    </ul>
  </div>
</template>

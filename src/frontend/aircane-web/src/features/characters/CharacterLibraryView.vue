<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useCharacterStore } from './store'
import { type CharacterDto, type CreateCharacterRequest, type UpdateCharacterRequest } from './api'
import CharacterForm from './components/CharacterForm.vue'
import CharacterList from './components/CharacterList.vue'
import ImportCharacterModal from './components/ImportCharacterModal.vue'
import { listCampaigns, type CampaignDto } from '@/features/campaigns/api'

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

const store = useCharacterStore()

// ---------------------------------------------------------------------------
// UI state
// ---------------------------------------------------------------------------

/** null = create mode; CharacterDto = edit mode */
const editingCharacter = ref<CharacterDto | null>(null)
const showForm = ref(false)
const showImportModal = ref(false)

/** The campaigns used for the filter control and the per-character assign select. */
const campaigns = ref<CampaignDto[]>([])

/** 'all' | 'unassigned' | <campaignId> */
const selectedCampaignFilter = ref<string>('all')

onMounted(async () => {
  await store.fetchAllCharacters()
  try {
    campaigns.value = await listCampaigns()
  } catch {
    // Campaigns are only needed for the filter/assign controls; a failure here
    // should not block the library list itself.
    campaigns.value = []
  }
})

/** The library list after applying the active campaign filter. */
const filtered = computed<CharacterDto[]>(() => {
  const filter = selectedCampaignFilter.value
  if (filter === 'all') return store.characters
  if (filter === 'unassigned') return store.characters.filter((c) => c.campaignName === null)
  return store.characters.filter((c) => c.campaignId === filter)
})

const detailStatTiles = computed(() => {
  const c = editingCharacter.value
  if (!c) return []
  let combat: Record<string, number> = {}
  try {
    combat = JSON.parse(c.canonicalJson)?.combat ?? {}
  } catch {
    combat = {}
  }
  return [
    { key: 'hit-points', label: 'Hit Points', value: combat.maxHitPoints ?? '—' },
    { key: 'armor-class', label: 'Armor Class', value: combat.armorClass ?? '—' },
    { key: 'speed', label: 'Speed', value: combat.speed ?? '—' },
    { key: 'proficiency', label: 'Proficiency', value: combat.proficiencyBonus ?? '—' },
  ]
})

// ---------------------------------------------------------------------------
// Create / edit form
// ---------------------------------------------------------------------------

function openCreateForm(): void {
  editingCharacter.value = null
  showForm.value = true
}

function openEditForm(character: CharacterDto): void {
  editingCharacter.value = character
  showForm.value = true
}

function closeForm(): void {
  showForm.value = false
  editingCharacter.value = null
}

async function handleFormSubmit(payload: CreateCharacterRequest | UpdateCharacterRequest): Promise<void> {
  if (editingCharacter.value) {
    await store.updateCharacter(editingCharacter.value.id, payload as UpdateCharacterRequest)
  } else {
    await store.createCharacter(payload as CreateCharacterRequest)
  }
  closeForm()
  // Refresh the authoritative list/labels after a create or edit.
  await store.fetchAllCharacters()
}

// ---------------------------------------------------------------------------
// Import modal
// ---------------------------------------------------------------------------

function openImportModal(): void {
  showImportModal.value = true
}

function closeImportModal(): void {
  showImportModal.value = false
}

async function handleImportCompleted(): Promise<void> {
  closeImportModal()
  await store.fetchAllCharacters()
}

// ---------------------------------------------------------------------------
// Assign / unassign / reassign
// ---------------------------------------------------------------------------

/** Handle a change on a character's campaign assignment select. '' = unassign. */
async function handleAssignmentChange(character: CharacterDto, event: Event): Promise<void> {
  const value = (event.target as HTMLSelectElement).value
  const campaignId = value === '' ? null : value
  if (campaignId === character.campaignId) return
  await store.setCharacterCampaign(character.id, campaignId)
}
</script>

<template>
  <div class="page-sections">
    <!-- Hero banner -->
    <section
      class="app-hero"
      v-bg-asset="{ url: '/assets/characters/characters-hero-background.png', fallback: '#0d0d2a' }"
    >
      <div class="app-hero__overlay">
        <h1 class="app-hero__title">Character Library</h1>
        <p class="app-hero__subtitle">
          Every character you own, assigned to a campaign or waiting in your library.
        </p>
      </div>
    </section>

    <!-- Page header -->
    <div class="flex items-center justify-between gap-4">
      <div class="flex items-center gap-3">
        <h2 class="text-xl font-semibold text-white">Your Characters</h2>

        <!-- Campaign filter -->
        <label class="sr-only" for="campaign-filter">Filter by campaign</label>
        <select
          id="campaign-filter"
          v-model="selectedCampaignFilter"
          class="rounded-lg border border-surface-700/50 bg-surface-850 px-3 py-1.5 text-sm text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        >
          <option value="all">All campaigns</option>
          <option value="unassigned">Unassigned</option>
          <option v-for="campaign in campaigns" :key="campaign.id" :value="campaign.id">
            {{ campaign.name }}
          </option>
        </select>
      </div>

      <div v-if="!showForm" class="flex items-center gap-2">
        <button
          class="import-button import-button--json inline-flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
          v-bg-asset="{ url: '/assets/characters/import-json-button-art.png', fallback: 'transparent', size: '100% 100%' }"
          @click="openImportModal"
        >
          <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
            <path
              fill-rule="evenodd"
              d="M10 3a.75.75 0 01.75.75v8.614l2.955-3.129a.75.75 0 011.09 1.03l-4.25 4.5a.75.75 0 01-1.09 0l-4.25-4.5a.75.75 0 111.09-1.03L9.25 12.364V3.75A.75.75 0 0110 3z"
              clip-rule="evenodd"
            />
            <path
              d="M3.5 12.75a.75.75 0 00-1.5 0v2.5A2.75 2.75 0 004.75 18h10.5A2.75 2.75 0 0018 15.25v-2.5a.75.75 0 00-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5z"
            />
          </svg>
          Import Character
        </button>

        <button
          class="new-character-button focus:outline-none focus:ring-2 focus:ring-aircane-400"
          v-bg-asset="{ url: '/assets/characters/new-character-button-art.png', fallback: 'transparent', size: '100% 100%' }"
          @click="openCreateForm"
        >
          <span class="new-character-label">+ New Character</span>
        </button>
      </div>
    </div>

    <!-- Global error banner -->
    <div
      v-if="store.error"
      role="alert"
      class="flex items-start gap-3 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
    >
      <svg
        class="mt-0.5 h-4 w-4 shrink-0 text-red-400"
        xmlns="http://www.w3.org/2000/svg"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 100-16 8 8 0 000 16zm-.75-9.25a.75.75 0 011.5 0v3.5a.75.75 0 01-1.5 0v-3.5zm.75 6a.75.75 0 100-1.5.75.75 0 000 1.5z"
          clip-rule="evenodd"
        />
      </svg>
      <span>{{ store.error }}</span>
    </div>

    <!-- Create / Edit form panel -->
    <section
      v-if="showForm"
      aria-labelledby="character-form-heading"
      class="grid gap-6 lg:grid-cols-[1fr_320px]"
    >
      <div class="rounded-xl border border-surface-700/50 bg-surface-850 p-6">
        <h2 id="character-form-heading" class="mb-6 text-lg font-semibold text-white">
          {{ editingCharacter ? 'Edit Character' : 'New Character' }}
        </h2>

        <!-- No campaign-id: characters created from the library are Unassigned. -->
        <CharacterForm
          :character="editingCharacter ?? undefined"
          @submit="handleFormSubmit"
          @cancel="closeForm"
        />
      </div>

      <aside
        v-if="editingCharacter"
        class="detail-panel"
        aria-label="Character stats"
        v-bg-asset="{ url: '/assets/characters/character-detail-side-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
      >
        <div class="detail-panel-body">
          <div class="stat-tiles">
            <div
              v-for="tile in detailStatTiles"
              :key="tile.key"
              class="stat-tile"
              v-bg-asset="{ url: `/assets/characters/character-stat-tile-${tile.key}.png`, fallback: '#0d0d2a', size: '100% 100%' }"
            >
              <span class="stat-value">{{ tile.value }}</span>
              <span class="stat-label">{{ tile.label }}</span>
            </div>
          </div>
        </div>
      </aside>
    </section>

    <!-- Per-character assignment controls -->
    <section
      v-if="!showForm && filtered.length > 0"
      aria-label="Campaign assignments"
      class="grid gap-2 rounded-xl border border-surface-700/50 bg-surface-850 p-4"
    >
      <h3 class="text-sm font-semibold text-gray-300">Campaign assignments</h3>
      <ul class="grid list-none gap-2 p-0">
        <li
          v-for="character in filtered"
          :key="character.id"
          class="flex items-center justify-between gap-3 rounded-lg bg-surface-900/40 px-3 py-2"
        >
          <span class="truncate text-sm text-white">{{ character.name }}</span>
          <label class="sr-only" :for="`assign-${character.id}`">
            Assign {{ character.name }} to a campaign
          </label>
          <select
            :id="`assign-${character.id}`"
            class="rounded-lg border border-surface-700/50 bg-surface-850 px-3 py-1.5 text-sm text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            :value="character.campaignId ?? ''"
            @change="handleAssignmentChange(character, $event)"
          >
            <option value="">Unassign</option>
            <option v-for="campaign in campaigns" :key="campaign.id" :value="campaign.id">
              {{ campaign.name }}
            </option>
          </select>
        </li>
      </ul>
    </section>

    <!-- Character list -->
    <CharacterList
      :characters="filtered"
      :show-campaign-label="true"
      @edit="openEditForm"
      @view="openEditForm"
    />

    <!-- Import character modal (Upload File / D&D Beyond URL tabs). No campaign-id:
         imports from the library are Unassigned. -->
    <ImportCharacterModal
      :open="showImportModal"
      @completed="handleImportCompleted"
      @close="closeImportModal"
    />
  </div>
</template>

<style scoped>
.import-button {
  background-size: 100% 100%;
  background-repeat: no-repeat;
  background-color: var(--color-purple);
  border: var(--border-gold);
  box-shadow: var(--glow-purple);
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.7);
  transition: box-shadow 0.2s ease;
}

.import-button:hover {
  box-shadow: var(--glow-purple-lg);
}

.new-character-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 180px;
  min-height: 44px;
  padding: 0 1.5rem;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  background-color: var(--color-purple);
  background-size: 100% 100%;
  background-repeat: no-repeat;
  box-shadow: var(--glow-purple);
  transition: box-shadow 0.2s ease;
}

.new-character-button:hover {
  box-shadow: var(--glow-purple-lg);
}

.new-character-label {
  font-size: 0.9375rem;
  font-weight: 700;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

.detail-panel {
  position: relative;
  min-height: 260px;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  background-size: cover;
  background-position: left center;
  background-repeat: no-repeat;
}

.detail-panel-body {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 60%;
  padding: 1rem;
}

.stat-tiles {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  height: 100%;
}

.stat-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  padding: 8px;
  background-size: 100% 100%;
  background-repeat: no-repeat;
  border: var(--border-gold-dim);
  border-radius: var(--border-radius-sm);
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  line-height: 1;
  color: #fff;
}

.stat-label {
  font-size: 0.6875rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--color-gold);
}
</style>

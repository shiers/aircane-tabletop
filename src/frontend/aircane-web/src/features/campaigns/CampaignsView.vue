<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useCampaignStore } from './store'
import { AiRole, AiAuthority, type CampaignDto, type CreateCampaignRequest, type UpdateCampaignRequest } from './api'
import CampaignForm from './components/CampaignForm.vue'
import CampaignList from './components/CampaignList.vue'

const store = useCampaignStore()

/** null = create mode; CampaignDto = edit mode */
const editingCampaign = ref<CampaignDto | null>(null)
const showForm = ref(false)

onMounted(() => {
  store.fetchCampaigns()
})

function openCreateForm(): void {
  editingCampaign.value = null
  showForm.value = true
}

function openEditForm(campaign: CampaignDto): void {
  editingCampaign.value = campaign
  showForm.value = true
}

function closeForm(): void {
  showForm.value = false
  editingCampaign.value = null
}

async function handleFormSubmit(payload: CreateCampaignRequest | UpdateCampaignRequest): Promise<void> {
  if (editingCampaign.value) {
    await store.updateCampaign(editingCampaign.value.id, payload as UpdateCampaignRequest)
  } else {
    await store.createCampaign(payload as CreateCampaignRequest)
  }
  closeForm()
}
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6">
    <!-- Hero banner -->
    <section
      class="hero"
      v-bg-asset="{ url: '/assets/campaigns/campaigns-hero-background.png', fallback: '#0d0d2a' }"
    >
      <div class="hero-heading">
        <h1 class="text-2xl font-bold text-white sm:text-3xl">Campaigns</h1>
        <p class="mt-1 text-sm text-gray-200">Create, manage, and jump back into your adventures.</p>
      </div>
    </section>

    <!-- Page header -->
    <div class="flex items-center justify-between">
      <h2 class="text-xl font-semibold text-white">Your Campaigns</h2>
      <button
        v-if="!showForm"
        class="btn-primary"
        @click="openCreateForm"
      >
        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 4v16m8-8H4" />
        </svg>
        New Campaign
      </button>
    </div>

    <div class="space-y-6">
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
        aria-labelledby="campaign-form-heading"
        class="card"
      >
        <h2 id="campaign-form-heading" class="mb-4 text-lg font-semibold text-white">
          {{ editingCampaign ? 'Edit Campaign' : 'New Campaign' }}
        </h2>

        <CampaignForm
          :campaign="editingCampaign ?? undefined"
          @submit="handleFormSubmit"
          @cancel="closeForm"
        />
      </section>

      <!-- Campaign overview + recent sessions panels -->
      <div v-if="!showForm" class="grid gap-6 lg:grid-cols-2">
        <section
          class="panel panel--overview"
          aria-labelledby="campaign-overview-heading"
          v-bg-asset="{ url: '/assets/campaigns/campaign-overview-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
        >
          <div class="panel-body">
            <h2 id="campaign-overview-heading" class="panel-title">Campaign Overview</h2>
            <p class="panel-text">Pick up where you left off in your most recent adventure.</p>
            <button
              type="button"
              class="continue-button"
              v-bg-asset="{ url: '/assets/campaigns/continue-button-art.png', fallback: 'transparent', size: '100% 100%' }"
              @click="store.fetchCampaigns"
            >
              <span class="continue-label">Continue</span>
            </button>
          </div>
        </section>

        <section
          class="panel panel--sessions"
          aria-labelledby="recent-sessions-heading"
          v-bg-asset="{ url: '/assets/campaigns/recent-sessions-panel-art.png', fallback: '#0d0d2a', position: 'left center' }"
        >
          <div class="panel-body">
            <h2 id="recent-sessions-heading" class="panel-title">Recent Sessions</h2>
            <p class="panel-text">Your latest play sessions appear here as you run them.</p>
          </div>
        </section>
      </div>

      <!-- Campaign list -->
      <CampaignList @edit="openEditForm" />
    </div>
  </div>
</template>

<style scoped>
/* Hero banner */
.hero {
  position: relative;
  width: 100%;
  min-height: 180px;
  overflow: hidden;
  border-radius: var(--border-radius-md);
  background-size: cover;
  background-position: center;
}

.hero-heading {
  position: absolute;
  top: 50%;
  left: 5%;
  right: 5%;
  transform: translateY(-50%);
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

/* Panel art sections (background-size: cover; background-position: left center) */
.panel {
  position: relative;
  min-height: 160px;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  background-size: cover;
  background-position: left center;
  background-repeat: no-repeat;
}

/* Content overlaid over the right 50% */
.panel-body {
  margin-left: 50%;
  padding: 1.25rem 1.25rem 1.25rem 0.75rem;
}

.panel-title {
  font-size: 1.125rem;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

.panel-text {
  margin-top: 0.5rem;
  font-size: 0.8125rem;
  color: #e5e7eb;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

/* Continue button — art-independent CSS base so it is always clickable. */
.continue-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 140px;
  min-height: 40px;
  margin-top: 0.75rem;
  padding: 0 1.25rem;
  border-radius: var(--border-radius-md);
  border: var(--border-gold);
  background-color: var(--color-purple);
  box-shadow: var(--glow-purple);
  background-repeat: no-repeat;
  transition: box-shadow 0.2s ease;
}

.continue-button:hover {
  box-shadow: var(--glow-purple);
}

.continue-label {
  font-size: 0.875rem;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}
</style>

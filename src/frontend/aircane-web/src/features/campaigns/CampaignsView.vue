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
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Page header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-5xl items-center justify-between">
        <div class="flex items-center gap-3">
          <RouterLink
            to="/"
            class="text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            aria-label="Back to home"
          >
            ← Home
          </RouterLink>
          <span class="text-gray-700" aria-hidden="true">/</span>
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Campaigns</h1>
        </div>

        <button
          v-if="!showForm"
          class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="openCreateForm"
        >
          + New Campaign
        </button>
      </div>
    </header>

    <div class="mx-auto max-w-5xl space-y-6 px-6 py-8">
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
        class="rounded-xl border border-gray-800 bg-gray-900 p-6"
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

      <!-- Campaign list -->
      <CampaignList @edit="openEditForm" />
    </div>
  </main>
</template>

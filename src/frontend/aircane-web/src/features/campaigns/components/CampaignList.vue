<script setup lang="ts">
import { ref } from 'vue'
import { useCampaignStore } from '../store'
import { aiRoleLabels, aiAuthorityLabels, type CampaignDto } from '../api'

const emit = defineEmits<{
  (e: 'edit', campaign: CampaignDto): void
}>()

const store = useCampaignStore()
const deletingId = ref<string | null>(null)

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

async function handleDelete(campaign: CampaignDto): Promise<void> {
  if (!confirm(`Delete "${campaign.name}"? This cannot be undone.`)) return
  deletingId.value = campaign.id
  try {
    await store.deleteCampaign(campaign.id)
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <section aria-labelledby="campaigns-list-heading">
    <h2 id="campaigns-list-heading" class="mb-4 text-lg font-semibold text-white">
      Your Campaigns
    </h2>

    <!-- Loading skeleton -->
    <div v-if="store.loading && store.campaigns.length === 0" class="space-y-2" aria-busy="true" aria-label="Loading campaigns">
      <div v-for="n in 3" :key="n" class="h-14 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!store.loading && store.campaigns.length === 0"
      class="rounded-xl border border-dashed border-gray-700 py-16 text-center text-gray-500"
    >
      <p class="text-sm">No campaigns yet. Create one above to get started.</p>
    </div>

    <!-- Campaign table -->
    <div v-else class="overflow-x-auto rounded-xl border border-gray-800">
      <table class="w-full text-left text-sm text-gray-300" aria-label="Campaigns">
        <thead class="border-b border-gray-800 bg-gray-900 text-xs uppercase tracking-wider text-gray-500">
          <tr>
            <th scope="col" class="px-4 py-3">Name</th>
            <th scope="col" class="px-4 py-3">System / Ruleset</th>
            <th scope="col" class="px-4 py-3">AI Role</th>
            <th scope="col" class="px-4 py-3">AI Authority</th>
            <th scope="col" class="px-4 py-3">Created</th>
            <th scope="col" class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-800 bg-gray-950">
          <tr v-for="campaign in store.campaigns" :key="campaign.id" class="hover:bg-gray-900">
            <!-- Name -->
            <td class="px-4 py-3 font-medium text-white">{{ campaign.name }}</td>

            <!-- System / Ruleset -->
            <td class="px-4 py-3">
              {{ [campaign.gameSystem, campaign.ruleset].filter(Boolean).join(' · ') }}
            </td>

            <!-- AI Role -->
            <td class="px-4 py-3">{{ aiRoleLabels[campaign.aiRole] }}</td>

            <!-- AI Authority -->
            <td class="px-4 py-3">{{ aiAuthorityLabels[campaign.aiAuthority] }}</td>

            <!-- Created date -->
            <td class="px-4 py-3 text-gray-400">{{ formatDate(campaign.createdAt) }}</td>

            <!-- Actions -->
            <td class="px-4 py-3 text-right">
              <div class="flex items-center justify-end gap-2">
                <button
                  :aria-label="`Edit ${campaign.name}`"
                  class="rounded px-2 py-1 text-xs font-medium text-blue-400 hover:bg-gray-800 hover:text-blue-300 focus:outline-none focus:ring-2 focus:ring-blue-500"
                  @click="emit('edit', campaign)"
                >
                  Edit
                </button>

                <button
                  :disabled="deletingId === campaign.id"
                  :aria-label="`Delete ${campaign.name}`"
                  class="rounded px-2 py-1 text-xs font-medium text-red-400 hover:bg-gray-800 hover:text-red-300 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                  @click="handleDelete(campaign)"
                >
                  <span v-if="deletingId === campaign.id">Deleting…</span>
                  <span v-else>Delete</span>
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

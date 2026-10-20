<script setup lang="ts">
/**
 * CampaignDetail - the page for one campaign. Route: /campaigns/:campaignId
 * Shows the campaign's summary and the adventures generated for it.
 */
import { ref, onMounted } from 'vue'
import { getCampaign, aiRoleLabels, aiAuthorityLabels, type CampaignDto } from './api'
import CampaignAdventures from './components/CampaignAdventures.vue'

const props = defineProps<{
  campaignId: string
}>()

const campaign = ref<CampaignDto | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    campaign.value = await getCampaign(props.campaignId)
  } catch (e) {
    const status = (e as { response?: { status?: number } })?.response?.status
    error.value = status === 404 ? 'Campaign not found.' : 'Could not load this campaign.'
  }
})
</script>

<template>
  <div class="mx-auto max-w-4xl space-y-6" data-testid="campaign-detail">
    <router-link to="/campaigns" class="text-sm text-gray-400 hover:text-white" data-testid="back-to-campaigns">
      ← Campaigns
    </router-link>

    <div v-if="error" role="alert" class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300">
      {{ error }}
    </div>

    <header v-else-if="campaign" class="space-y-1">
      <h1 class="text-2xl font-bold text-white" data-testid="campaign-name">{{ campaign.name }}</h1>
      <p class="text-sm text-gray-400">
        {{ [campaign.gameSystem, campaign.ruleset].filter(Boolean).join(' · ') }}
        · AI: {{ aiRoleLabels[campaign.aiRole] }}, {{ aiAuthorityLabels[campaign.aiAuthority] }}
      </p>
    </header>

    <CampaignAdventures :campaign-id="props.campaignId" :campaign-name="campaign?.name" />
  </div>
</template>

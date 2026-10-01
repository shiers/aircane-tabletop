import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
  listCampaigns,
  createCampaign,
  updateCampaign,
  deleteCampaign,
  type CampaignDto,
  type CreateCampaignRequest,
  type UpdateCampaignRequest,
} from './api'

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return 'An unexpected error occurred.'
}

export const useCampaignStore = defineStore('campaigns', () => {
  // ── State ──────────────────────────────────────────────────────────────────
  const campaigns = ref<CampaignDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions ────────────────────────────────────────────────────────────────

  /** Fetch all campaigns from the API. */
  async function fetchCampaigns(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      campaigns.value = await listCampaigns()
    } catch (err) {
      error.value = extractMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Create a new campaign and prepend it to the list. */
  async function createCampaignAction(request: CreateCampaignRequest): Promise<CampaignDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await createCampaign(request)
      campaigns.value.unshift(dto)
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Update an existing campaign and refresh it in the list. */
  async function updateCampaignAction(id: string, request: UpdateCampaignRequest): Promise<CampaignDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await updateCampaign(id, request)
      const idx = campaigns.value.findIndex((c) => c.id === id)
      if (idx !== -1) campaigns.value[idx] = dto
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Delete a campaign and remove it from the list. */
  async function deleteCampaignAction(id: string): Promise<void> {
    error.value = null
    try {
      await deleteCampaign(id)
      campaigns.value = campaigns.value.filter((c) => c.id !== id)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  return {
    // State
    campaigns,
    loading,
    error,
    // Actions
    fetchCampaigns,
    createCampaign: createCampaignAction,
    updateCampaign: updateCampaignAction,
    deleteCampaign: deleteCampaignAction,
  }
})

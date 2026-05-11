import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export enum AiRole {
  Assistant = 0,
  CoDm = 1,
  FullDm = 2,
  Hybrid = 3,
}

export enum AiAuthority {
  SuggestOnly = 0,
  AskBeforeApplying = 1,
  AutoApplySafeActions = 2,
  FullSessionControl = 3,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface CampaignDto {
  id: string
  name: string
  gameSystem: string
  ruleset: string
  gameSystemDefinitionId: string | null
  aiRole: AiRole
  aiAuthority: AiAuthority
  activeAdventureId: string | null
  createdAt: string
  updatedAt: string
}

export interface CreateCampaignRequest {
  name: string
  gameSystem: string
  ruleset: string
  gameSystemDefinitionId: string | null
  aiRole: AiRole
  aiAuthority: AiAuthority
}

export interface UpdateCampaignRequest {
  name?: string
  gameSystem?: string
  ruleset?: string
  gameSystemDefinitionId?: string | null
  aiRole?: AiRole
  aiAuthority?: AiAuthority
  activeAdventureId?: string | null
}

// ---------------------------------------------------------------------------
// Display helpers
// ---------------------------------------------------------------------------

export const aiRoleLabels: Record<AiRole, string> = {
  [AiRole.Assistant]: 'Assistant',
  [AiRole.CoDm]: 'Co-DM',
  [AiRole.FullDm]: 'Full DM',
  [AiRole.Hybrid]: 'Hybrid',
}

export const aiAuthorityLabels: Record<AiAuthority, string> = {
  [AiAuthority.SuggestOnly]: 'Suggest Only',
  [AiAuthority.AskBeforeApplying]: 'Ask Before Applying',
  [AiAuthority.AutoApplySafeActions]: 'Auto-Apply Safe Actions',
  [AiAuthority.FullSessionControl]: 'Full Session Control',
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/** Create a new campaign. */
export async function createCampaign(request: CreateCampaignRequest): Promise<CampaignDto> {
  const response = await apiClient.post<CampaignDto>('/api/campaigns', request)
  return response.data
}

/** List all campaigns. */
export async function listCampaigns(): Promise<CampaignDto[]> {
  const response = await apiClient.get<CampaignDto[]>('/api/campaigns')
  return response.data
}

/** Get a single campaign by ID. */
export async function getCampaign(id: string): Promise<CampaignDto> {
  const response = await apiClient.get<CampaignDto>(`/api/campaigns/${id}`)
  return response.data
}

/** Update a campaign. */
export async function updateCampaign(id: string, request: UpdateCampaignRequest): Promise<CampaignDto> {
  const response = await apiClient.put<CampaignDto>(`/api/campaigns/${id}`, request)
  return response.data
}

/** Delete a campaign by ID. */
export async function deleteCampaign(id: string): Promise<void> {
  await apiClient.delete(`/api/campaigns/${id}`)
}

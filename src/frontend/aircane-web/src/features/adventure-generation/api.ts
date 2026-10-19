import apiClient from '@/shared/api/client'

export type AdventureMode = 'Solo' | 'Group'

export interface GenerateAdventureRequest {
  mode: AdventureMode
  ruleset: string
  gameSystem: string
  partySize: number
  averageLevel: number
  tone: string
  length: string
  difficulty: string
  combatRatio: number
  explorationRatio: number
  roleplayRatio: number
  setting?: string | null
  characterIds?: string[] | null
}

export interface GenerateAdventureResponse {
  jobId: string
  status: string
  message: string
}

// --- Draft Review Types ---

export interface AdventurePitch {
  title: string
  hook: string
  summary: string
}

export interface SceneSummary {
  title: string
  description: string
  sceneType: string
}

export interface AdventureOutline {
  sceneSummaries: SceneSummary[]
}

export interface GeneratedScene {
  sceneId: string
  title: string
  description: string
  sceneType: string
  connectsTo: string[]
  /** Player-facing goals; empty for adventures generated before objectives existed. */
  objectives?: string[]
  readAloudText?: string | null
  dmNotes?: string | null
}

export interface GeneratedNpc {
  name: string
  role: string
  personality: string
  statsSummary: string
  sceneIds: string[]
  faction?: string | null
}

export interface EncounterCreature {
  name: string
  count: number
  challengeRating: string
}

export interface GeneratedEncounter {
  title: string
  sceneId: string
  enemies: EncounterCreature[]
  difficulty: string
  tactics: string
  environment?: string | null
}

export interface TreasureItem {
  name: string
  description: string
  sceneId: string
  value: number
}

export interface AdventureTreasure {
  goldTotal: number
  items: TreasureItem[]
  magicItems: TreasureItem[]
}

export interface AdventureSecret {
  title: string
  content: string
  sceneId: string
  discoveryMethod: string
}

export interface AdventureHandout {
  title: string
  content: string
  sceneId: string
}

export interface FailForwardPath {
  trigger: string
  resolution: string
  sceneId: string
}

export interface AdventureClues {
  secrets: AdventureSecret[]
  handouts: AdventureHandout[]
  failForwardPaths: FailForwardPath[]
}

export interface AdventureDraft {
  id: string
  title: string
  status: string
  /** The campaign this adventure belongs to, if any. */
  campaignId?: string | null
  pitch?: AdventurePitch | null
  outline?: AdventureOutline | null
  scenes?: GeneratedScene[] | null
  npcs?: GeneratedNpc[] | null
  encounters?: GeneratedEncounter[] | null
  treasure?: AdventureTreasure | null
  clues?: AdventureClues | null
  createdAt: string
  updatedAt: string
}

// --- API Functions ---

export async function generateAdventure(
  request: GenerateAdventureRequest,
  campaignId?: string | null,
): Promise<GenerateAdventureResponse> {
  const response = await apiClient.post<GenerateAdventureResponse>(
    '/api/adventures/generate',
    request,
    { params: campaignId ? { campaignId } : undefined },
  )
  return response.data
}

export async function getAdventureDraft(adventureId: string): Promise<AdventureDraft> {
  const response = await apiClient.get<AdventureDraft>(`/api/adventures/${adventureId}/draft`)
  return response.data
}

/** One adventure card on a campaign's page. */
export interface CampaignAdventure {
  id: string
  title: string
  status: 'Draft' | 'Approved' | 'Active' | 'Rejected'
  gameSystem: string
  ruleset: string
  mode: string | null
  length: string | null
  averageLevel: number | null
  createdAt: string
  /** The in-progress run of this adventure, if any (so the card can offer Resume). */
  activeSession?: ActiveSession | null
}

/** How far through an in-progress run is. */
export interface ActiveSession {
  sessionId: string
  currentSceneNumber: number
  totalScenes: number
  status: string
}

/**
 * True when the adventure was generated for a group. The list endpoints send the mode as text
 * ("Solo" / "Group"), and older adventures may have none: those are offered as solo.
 */
export function isGroupAdventure(mode: string | null | undefined): boolean {
  return mode?.trim().toLowerCase() === 'group'
}

/** The adventures generated for a campaign, newest first. */
export async function listCampaignAdventures(campaignId: string): Promise<CampaignAdventure[]> {
  const response = await apiClient.get<CampaignAdventure[]>(`/api/campaigns/${campaignId}/adventures`)
  return response.data
}

/** Adventures that belong to no campaign yet, newest first. */
export async function listUnassignedAdventures(): Promise<CampaignAdventure[]> {
  const response = await apiClient.get<CampaignAdventure[]>('/api/adventures', { params: { unassigned: true } })
  return response.data
}

/** Move an adventure into a campaign. */
export async function assignAdventureToCampaign(adventureId: string, campaignId: string): Promise<CampaignAdventure> {
  const response = await apiClient.patch<CampaignAdventure>(`/api/adventures/${adventureId}/assign`, { campaignId })
  return response.data
}

export async function deleteAdventure(adventureId: string): Promise<void> {
  await apiClient.delete(`/api/adventures/${adventureId}`)
}

export async function approveAdventure(adventureId: string): Promise<void> {
  await apiClient.post(`/api/adventures/${adventureId}/approve`)
}

/**
 * The generation mode of one adventure ("Solo" / "Group"), or null when it cannot be determined.
 * The draft endpoint carries the campaign but not the mode, so this reads it from the list
 * endpoints, which do. Never throws: a page that only wants to pick a button must not fail on this.
 */
export async function lookupAdventureMode(adventureId: string, campaignId?: string | null): Promise<string | null> {
  try {
    const list = campaignId ? await listCampaignAdventures(campaignId) : await listUnassignedAdventures()
    return list.find((a) => a.id === adventureId)?.mode ?? null
  } catch {
    return null
  }
}

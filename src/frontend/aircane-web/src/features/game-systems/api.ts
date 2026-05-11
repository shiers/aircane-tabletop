import apiClient from '@/shared/api/client'
import type {
  GameSystemDefinitionSummary,
  GameSystemDefinitionDetail,
  ValidationResult,
  StarterTemplate,
} from './types'

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/** List all active game system definitions. */
export async function listGameSystems(): Promise<GameSystemDefinitionSummary[]> {
  const response = await apiClient.get<GameSystemDefinitionSummary[]>('/api/game-systems')
  return response.data
}

/** Get a single game system definition by ID. */
export async function getGameSystem(id: string): Promise<GameSystemDefinitionDetail> {
  const response = await apiClient.get<GameSystemDefinitionDetail>(`/api/game-systems/${id}`)
  return response.data
}

/** Create a new game system definition. */
export async function createGameSystem(definitionJson: string): Promise<GameSystemDefinitionDetail> {
  const response = await apiClient.post<GameSystemDefinitionDetail>('/api/game-systems', {
    definitionJson,
  })
  return response.data
}

/** Update an existing game system definition (creates a new version). */
export async function updateGameSystem(id: string, definitionJson: string): Promise<GameSystemDefinitionDetail> {
  const response = await apiClient.put<GameSystemDefinitionDetail>(`/api/game-systems/${id}`, {
    definitionJson,
  })
  return response.data
}

/** Deactivate a game system definition. */
export async function deactivateGameSystem(id: string): Promise<void> {
  await apiClient.delete(`/api/game-systems/${id}`)
}

/** Import a game system definition from a file. */
export async function importGameSystem(file: File): Promise<GameSystemDefinitionDetail> {
  const formData = new FormData()
  formData.append('file', file)
  const response = await apiClient.post<GameSystemDefinitionDetail>('/api/game-systems/import', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return response.data
}

/** Export a game system definition as JSON or YAML. */
export async function exportGameSystem(id: string, format: 'json' | 'yaml' = 'json'): Promise<Blob> {
  const response = await apiClient.get(`/api/game-systems/${id}/export`, {
    params: { format },
    responseType: 'blob',
  })
  return response.data
}

/** Validate a game system definition without saving. */
export async function validateGameSystem(definitionJson: string): Promise<ValidationResult> {
  const response = await apiClient.post<ValidationResult>('/api/game-systems/validate', {
    definitionJson,
  })
  return response.data
}

/** List available starter templates. */
export async function listTemplates(): Promise<StarterTemplate[]> {
  const response = await apiClient.get<StarterTemplate[]>('/api/game-systems/templates')
  return response.data
}

/** Preview a dice roll for a game system definition. */
export async function previewRoll(id: string, formula: string): Promise<{ results: number[]; total: number }> {
  const response = await apiClient.post<{ results: number[]; total: number }>(
    `/api/game-systems/${id}/preview-roll`,
    { formula },
  )
  return response.data
}

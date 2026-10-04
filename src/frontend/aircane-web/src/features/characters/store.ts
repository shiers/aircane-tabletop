import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
  listCharacters,
  createCharacter,
  updateCharacter,
  deleteCharacter,
  importCharacterFromJson,
  importCharacterFromSource,
  importCharacterFromDndBeyondUrl,
  type CharacterDto,
  type CreateCharacterRequest,
  type UpdateCharacterRequest,
  type ImportCharacterJsonRequest,
  type CharacterImportResult,
  type SourceImportResponse,
  type DndBeyondUrlImportRequest,
} from './api'

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return 'An unexpected error occurred.'
}

export const useCharacterStore = defineStore('characters', () => {
  // ── State ──────────────────────────────────────────────────────────────────
  const characters = ref<CharacterDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions ────────────────────────────────────────────────────────────────

  /** Fetch all characters for a campaign. */
  async function fetchCharacters(campaignId: string, participantId?: string): Promise<void> {
    loading.value = true
    error.value = null
    try {
      characters.value = await listCharacters(campaignId, participantId)
    } catch (err) {
      error.value = extractMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Create a new character and prepend it to the list. */
  async function createCharacterAction(request: CreateCharacterRequest): Promise<CharacterDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await createCharacter(request)
      characters.value.unshift(dto)
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Update an existing character and refresh it in the list. */
  async function updateCharacterAction(id: string, request: UpdateCharacterRequest): Promise<CharacterDto> {
    loading.value = true
    error.value = null
    try {
      const dto = await updateCharacter(id, request)
      const idx = characters.value.findIndex((c) => c.id === id)
      if (idx !== -1) characters.value[idx] = dto
      return dto
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Delete a character and remove it from the list. */
  async function deleteCharacterAction(id: string): Promise<void> {
    error.value = null
    try {
      await deleteCharacter(id)
      characters.value = characters.value.filter((c) => c.id !== id)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Import a character from a canonical JSON string. Returns the full result including errors. */
  async function importCharacterFromJsonAction(
    request: ImportCharacterJsonRequest,
  ): Promise<CharacterImportResult> {
    loading.value = true
    error.value = null
    try {
      const result = await importCharacterFromJson(request)
      if (result.success && result.character) {
        characters.value.unshift(result.character)
      }
      return result
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Import a character via the source-adapter path. Returns the review envelope. */
  async function importCharacterFromSourceAction(
    request: ImportCharacterJsonRequest,
    options?: { source?: string; gameSystemDefinitionId?: string },
  ): Promise<SourceImportResponse> {
    loading.value = true
    error.value = null
    try {
      return await importCharacterFromSource(request, options)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Import a character from a D&D Beyond character URL. Returns the review envelope. */
  async function importCharacterFromDndBeyondUrlAction(
    request: DndBeyondUrlImportRequest,
  ): Promise<SourceImportResponse> {
    loading.value = true
    error.value = null
    try {
      return await importCharacterFromDndBeyondUrl(request)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  return {
    // State
    characters,
    loading,
    error,
    // Actions
    fetchCharacters,
    createCharacter: createCharacterAction,
    updateCharacter: updateCharacterAction,
    deleteCharacter: deleteCharacterAction,
    importCharacterFromJson: importCharacterFromJsonAction,
    importCharacterFromSource: importCharacterFromSourceAction,
    importCharacterFromDndBeyondUrl: importCharacterFromDndBeyondUrlAction,
  }
})

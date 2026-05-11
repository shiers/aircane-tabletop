import { defineStore } from 'pinia'
import { ref } from 'vue'
import {
  listGameSystems,
  getGameSystem,
  createGameSystem,
  updateGameSystem,
  deactivateGameSystem,
  importGameSystem,
  exportGameSystem,
  validateGameSystem,
  listTemplates,
} from '../api'
import type {
  GameSystemDefinitionSummary,
  GameSystemDefinitionDetail,
  ValidationResult,
  StarterTemplate,
} from '../types'

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return 'An unexpected error occurred.'
}

export const useGameSystemStore = defineStore('gameSystems', () => {
  // ── State ──────────────────────────────────────────────────────────────────
  const definitions = ref<GameSystemDefinitionSummary[]>([])
  const activeDefinition = ref<GameSystemDefinitionDetail | null>(null)
  const templates = ref<StarterTemplate[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions ────────────────────────────────────────────────────────────────

  /** Fetch all active game system definitions. */
  async function fetchAll(): Promise<void> {
    loading.value = true
    error.value = null
    try {
      definitions.value = await listGameSystems()
    } catch (err) {
      error.value = extractMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Fetch a single game system definition by ID. */
  async function fetchById(id: string): Promise<GameSystemDefinitionDetail | null> {
    loading.value = true
    error.value = null
    try {
      const detail = await getGameSystem(id)
      activeDefinition.value = detail
      return detail
    } catch (err) {
      error.value = extractMessage(err)
      return null
    } finally {
      loading.value = false
    }
  }

  /** Create a new game system definition. */
  async function create(definitionJson: string): Promise<GameSystemDefinitionDetail> {
    loading.value = true
    error.value = null
    try {
      const detail = await createGameSystem(definitionJson)
      definitions.value.unshift({
        id: detail.id,
        identifier: detail.identifier,
        name: detail.name,
        version: detail.version,
        publisher: detail.publisher,
        genre: detail.genre,
        description: detail.description,
        license: detail.license,
        isBuiltIn: detail.isBuiltIn,
        isActive: detail.isActive,
        createdAt: detail.createdAt,
        updatedAt: detail.updatedAt,
      })
      return detail
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Update an existing game system definition. */
  async function update(id: string, definitionJson: string): Promise<GameSystemDefinitionDetail> {
    loading.value = true
    error.value = null
    try {
      const detail = await updateGameSystem(id, definitionJson)
      const idx = definitions.value.findIndex((d) => d.id === id)
      if (idx !== -1) {
        definitions.value[idx] = {
          id: detail.id,
          identifier: detail.identifier,
          name: detail.name,
          version: detail.version,
          publisher: detail.publisher,
          genre: detail.genre,
          description: detail.description,
          license: detail.license,
          isBuiltIn: detail.isBuiltIn,
          isActive: detail.isActive,
          createdAt: detail.createdAt,
          updatedAt: detail.updatedAt,
        }
      }
      if (activeDefinition.value?.id === id) {
        activeDefinition.value = detail
      }
      return detail
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Deactivate a game system definition. */
  async function deactivate(id: string): Promise<void> {
    error.value = null
    try {
      await deactivateGameSystem(id)
      definitions.value = definitions.value.filter((d) => d.id !== id)
      if (activeDefinition.value?.id === id) {
        activeDefinition.value = null
      }
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Import a game system definition from a file. */
  async function importFile(file: File): Promise<GameSystemDefinitionDetail> {
    loading.value = true
    error.value = null
    try {
      const detail = await importGameSystem(file)
      definitions.value.unshift({
        id: detail.id,
        identifier: detail.identifier,
        name: detail.name,
        version: detail.version,
        publisher: detail.publisher,
        genre: detail.genre,
        description: detail.description,
        license: detail.license,
        isBuiltIn: detail.isBuiltIn,
        isActive: detail.isActive,
        createdAt: detail.createdAt,
        updatedAt: detail.updatedAt,
      })
      return detail
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
    }
  }

  /** Export a game system definition and trigger download. */
  async function exportFile(id: string, format: 'json' | 'yaml' = 'json'): Promise<void> {
    error.value = null
    try {
      const blob = await exportGameSystem(id, format)
      const def = definitions.value.find((d) => d.id === id)
      const filename = `${def?.identifier ?? 'game-system'}.${format}`
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = filename
      a.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Validate a game system definition without saving. */
  async function validate(definitionJson: string): Promise<ValidationResult> {
    error.value = null
    try {
      return await validateGameSystem(definitionJson)
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Fetch available starter templates. */
  async function fetchTemplates(): Promise<void> {
    error.value = null
    try {
      templates.value = await listTemplates()
    } catch (err) {
      error.value = extractMessage(err)
    }
  }

  return {
    // State
    definitions,
    activeDefinition,
    templates,
    loading,
    error,
    // Actions
    fetchAll,
    fetchById,
    create,
    update,
    deactivate,
    importFile,
    exportFile,
    validate,
    fetchTemplates,
  }
})

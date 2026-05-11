import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useGameSystemStore } from '../stores/useGameSystemStore'

// Mock the API module
vi.mock('../api', () => ({
  listGameSystems: vi.fn(),
  getGameSystem: vi.fn(),
  createGameSystem: vi.fn(),
  updateGameSystem: vi.fn(),
  deactivateGameSystem: vi.fn(),
  importGameSystem: vi.fn(),
  exportGameSystem: vi.fn(),
  validateGameSystem: vi.fn(),
  listTemplates: vi.fn(),
}))

import {
  listGameSystems,
  getGameSystem,
  createGameSystem,
  updateGameSystem,
  deactivateGameSystem,
  validateGameSystem,
  listTemplates,
} from '../api'

const mockSummary = {
  id: 'def-1',
  identifier: 'dnd-5e-2014',
  name: 'D&D 5e 2014',
  version: '1.0.0',
  publisher: 'Wizards of the Coast',
  genre: 'fantasy',
  description: 'The 2014 core rules.',
  license: 'built-in',
  isBuiltIn: true,
  isActive: true,
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
}

const mockDetail = {
  ...mockSummary,
  schemaVersion: 1,
  definitionJson: '{"schemaVersion":1,"metadata":{"name":"D&D 5e 2014"}}',
}

describe('useGameSystemStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('fetchAll', () => {
    it('fetches all definitions and sets state', async () => {
      vi.mocked(listGameSystems).mockResolvedValue([mockSummary])

      const store = useGameSystemStore()
      await store.fetchAll()

      expect(listGameSystems).toHaveBeenCalledOnce()
      expect(store.definitions).toEqual([mockSummary])
      expect(store.loading).toBe(false)
      expect(store.error).toBeNull()
    })

    it('sets error on failure', async () => {
      vi.mocked(listGameSystems).mockRejectedValue(new Error('Network error'))

      const store = useGameSystemStore()
      await store.fetchAll()

      expect(store.definitions).toEqual([])
      expect(store.error).toBe('Network error')
      expect(store.loading).toBe(false)
    })

    it('sets loading during fetch', async () => {
      let resolve: (value: unknown[]) => void
      vi.mocked(listGameSystems).mockReturnValue(new Promise((r) => { resolve = r }))

      const store = useGameSystemStore()
      const promise = store.fetchAll()

      expect(store.loading).toBe(true)
      resolve!([])
      await promise
      expect(store.loading).toBe(false)
    })
  })

  describe('fetchById', () => {
    it('fetches a definition and sets activeDefinition', async () => {
      vi.mocked(getGameSystem).mockResolvedValue(mockDetail)

      const store = useGameSystemStore()
      const result = await store.fetchById('def-1')

      expect(getGameSystem).toHaveBeenCalledWith('def-1')
      expect(store.activeDefinition).toEqual(mockDetail)
      expect(result).toEqual(mockDetail)
    })

    it('returns null and sets error on failure', async () => {
      vi.mocked(getGameSystem).mockRejectedValue(new Error('Not found'))

      const store = useGameSystemStore()
      const result = await store.fetchById('bad-id')

      expect(result).toBeNull()
      expect(store.error).toBe('Not found')
    })
  })

  describe('create', () => {
    it('creates a definition and adds to list', async () => {
      vi.mocked(createGameSystem).mockResolvedValue(mockDetail)

      const store = useGameSystemStore()
      const result = await store.create('{}')

      expect(createGameSystem).toHaveBeenCalledWith('{}')
      expect(result).toEqual(mockDetail)
      expect(store.definitions).toHaveLength(1)
      expect(store.definitions[0].id).toBe('def-1')
    })

    it('throws and sets error on failure', async () => {
      vi.mocked(createGameSystem).mockRejectedValue(new Error('Validation failed'))

      const store = useGameSystemStore()
      await expect(store.create('{}')).rejects.toThrow('Validation failed')
      expect(store.error).toBe('Validation failed')
    })
  })

  describe('update', () => {
    it('updates a definition in the list', async () => {
      const updated = { ...mockDetail, version: '2.0.0' }
      vi.mocked(updateGameSystem).mockResolvedValue(updated)

      const store = useGameSystemStore()
      store.definitions = [mockSummary]
      store.activeDefinition = mockDetail

      const result = await store.update('def-1', '{}')

      expect(updateGameSystem).toHaveBeenCalledWith('def-1', '{}')
      expect(result.version).toBe('2.0.0')
      expect(store.definitions[0].version).toBe('2.0.0')
      expect(store.activeDefinition?.version).toBe('2.0.0')
    })
  })

  describe('deactivate', () => {
    it('removes definition from list', async () => {
      vi.mocked(deactivateGameSystem).mockResolvedValue(undefined)

      const store = useGameSystemStore()
      store.definitions = [mockSummary]
      store.activeDefinition = mockDetail

      await store.deactivate('def-1')

      expect(deactivateGameSystem).toHaveBeenCalledWith('def-1')
      expect(store.definitions).toHaveLength(0)
      expect(store.activeDefinition).toBeNull()
    })
  })

  describe('validate', () => {
    it('returns validation result', async () => {
      const validResult = { isValid: true, errors: [] }
      vi.mocked(validateGameSystem).mockResolvedValue(validResult)

      const store = useGameSystemStore()
      const result = await store.validate('{}')

      expect(validateGameSystem).toHaveBeenCalledWith('{}')
      expect(result).toEqual(validResult)
    })

    it('returns validation errors', async () => {
      const invalidResult = {
        isValid: false,
        errors: [{ fieldPath: 'metadata.name', message: 'Required' }],
      }
      vi.mocked(validateGameSystem).mockResolvedValue(invalidResult)

      const store = useGameSystemStore()
      const result = await store.validate('{}')

      expect(result.isValid).toBe(false)
      expect(result.errors).toHaveLength(1)
    })
  })

  describe('fetchTemplates', () => {
    it('fetches templates', async () => {
      const mockTemplates = [
        { id: 't1', name: 'd20 System', description: 'D20-based', genre: 'fantasy', definitionJson: '{}' },
      ]
      vi.mocked(listTemplates).mockResolvedValue(mockTemplates)

      const store = useGameSystemStore()
      await store.fetchTemplates()

      expect(store.templates).toEqual(mockTemplates)
    })
  })
})

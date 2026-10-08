import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useCharacterStore } from '../store'
import { CharacterRole, type CharacterDto } from '../api'

// ── Mock the characters api ────────────────────────────────────────────────────
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    listAllCharacters: vi.fn(),
    setCharacterCampaign: vi.fn(),
  }
})

import * as charactersApi from '../api'

function makeCharacter(overrides: Partial<CharacterDto> = {}): CharacterDto {
  return {
    id: 'char-1',
    campaignId: null,
    campaignName: null,
    ownerParticipantId: null,
    role: CharacterRole.Player,
    name: 'Warduke',
    gameSystem: 'D&D 5e',
    ruleset: '2014',
    level: 3,
    canonicalJson: '{}',
    currentStateJson: '{}',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('character store — library actions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('fetchAllCharacters', () => {
    it('calls listAllCharacters (no params) and fills characters', async () => {
      const chars = [makeCharacter({ id: 'a', name: 'Alpha' }), makeCharacter({ id: 'b', name: 'Beta' })]
      vi.mocked(charactersApi.listAllCharacters).mockResolvedValue(chars)
      const store = useCharacterStore()

      await store.fetchAllCharacters()

      expect(charactersApi.listAllCharacters).toHaveBeenCalledTimes(1)
      expect(charactersApi.listAllCharacters).toHaveBeenCalledWith()
      expect(store.characters).toEqual(chars)
      expect(store.loading).toBe(false)
    })

    it('records an error message when the fetch fails', async () => {
      vi.mocked(charactersApi.listAllCharacters).mockRejectedValue(new Error('boom'))
      const store = useCharacterStore()

      await store.fetchAllCharacters()

      expect(store.error).toBe('boom')
      expect(store.loading).toBe(false)
    })
  })

  describe('setCharacterCampaign', () => {
    it('patches the matching row in place without changing list length', async () => {
      const store = useCharacterStore()
      store.characters = [
        makeCharacter({ id: 'a', name: 'Alpha' }),
        makeCharacter({ id: 'b', name: 'Beta' }),
      ]
      const updated = makeCharacter({ id: 'b', name: 'Beta', campaignId: 'camp-1', campaignName: 'Lost Mines' })
      vi.mocked(charactersApi.setCharacterCampaign).mockResolvedValue(updated)

      const result = await store.setCharacterCampaign('b', 'camp-1')

      expect(charactersApi.setCharacterCampaign).toHaveBeenCalledWith('b', 'camp-1')
      expect(result).toEqual(updated)
      expect(store.characters.length).toBe(2)
      expect(store.characters[1]).toEqual(updated)
      expect(store.characters[1].campaignName).toBe('Lost Mines')
    })

    it('sets error and rethrows on failure', async () => {
      const store = useCharacterStore()
      store.characters = [makeCharacter({ id: 'a' })]
      vi.mocked(charactersApi.setCharacterCampaign).mockRejectedValue(new Error('nope'))

      await expect(store.setCharacterCampaign('a', 'camp-1')).rejects.toThrow('nope')
      expect(store.error).toBe('nope')
    })
  })
})

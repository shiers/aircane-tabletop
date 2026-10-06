import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import CharacterList from '../CharacterList.vue'
import { useCharacterStore } from '../../store'
import type { CharacterDto } from '../../api'

// Mock the api module so the store's `deleteCharacter` binding is the mock (no network call).
vi.mock('../../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api')>()
  return { ...actual, deleteCharacter: vi.fn().mockResolvedValue(undefined) }
})

function makeCharacter(overrides: Partial<CharacterDto> = {}): CharacterDto {
  return {
    id: 'char-1',
    campaignId: null,
    campaignName: null,
    ownerParticipantId: null,
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

const stubs = {
  AircaneImg: { template: '<div><slot name="fallback" /></div>' },
  ThumbnailPlaceholder: true,
  AbilityScoreTile: true,
  RoleBadge: true,
}

function mountList(props: Record<string, unknown> = {}) {
  return mount(CharacterList, { props, global: { stubs } })
}

describe('CharacterList', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders the provided characters prop over the store list', () => {
    const store = useCharacterStore()
    store.characters = [makeCharacter({ id: 'store-only', name: 'StoreGuy' })]
    const wrapper = mountList({
      characters: [makeCharacter({ id: 'a', name: 'Alpha' }), makeCharacter({ id: 'b', name: 'Beta' })],
    })

    const cards = wrapper.findAll('.character-card')
    expect(cards.length).toBe(2)
    expect(wrapper.text()).toContain('Alpha')
    expect(wrapper.text()).toContain('Beta')
    expect(wrapper.text()).not.toContain('StoreGuy')
  })

  it('falls back to store.characters when no characters prop is given', () => {
    const store = useCharacterStore()
    store.characters = [makeCharacter({ id: 's1', name: 'FromStore' })]
    const wrapper = mountList()

    expect(wrapper.findAll('.character-card').length).toBe(1)
    expect(wrapper.text()).toContain('FromStore')
  })

  it('shows the empty state driven by effectiveList (prop empty, store non-empty)', () => {
    const store = useCharacterStore()
    store.loading = false
    store.characters = [makeCharacter({ id: 's1' })]
    const wrapper = mountList({ characters: [] })

    expect(wrapper.text()).toContain('No characters yet')
    expect(wrapper.findAll('.character-card').length).toBe(0)
  })

  it('shows the loading skeleton driven by effectiveList + store.loading', () => {
    const store = useCharacterStore()
    store.loading = true
    const wrapper = mountList({ characters: [] })

    expect(wrapper.find('[aria-busy="true"]').exists()).toBe(true)
  })

  it('renders the campaign pill only when show-campaign-label is true', () => {
    const assigned = makeCharacter({ id: 'a', name: 'Alpha', campaignName: 'Lost Mines' })
    const withLabel = mountList({ characters: [assigned], showCampaignLabel: true })
    expect(withLabel.find('.campaign-pill').exists()).toBe(true)
    expect(withLabel.find('.campaign-pill').text()).toBe('Lost Mines')

    const withoutLabel = mountList({ characters: [assigned] })
    expect(withoutLabel.find('.campaign-pill').exists()).toBe(false)
  })

  it('shows "Unassigned" on the pill when campaignName is null', () => {
    const unassigned = makeCharacter({ id: 'a', name: 'Alpha', campaignName: null })
    const wrapper = mountList({ characters: [unassigned], showCampaignLabel: true })
    const pill = wrapper.find('.campaign-pill')
    expect(pill.exists()).toBe(true)
    expect(pill.text()).toBe('Unassigned')
    expect(pill.classes()).toContain('campaign-pill--unassigned')
  })

  describe('delete action', () => {
    afterEach(() => {
      vi.restoreAllMocks()
    })

    it('renders a labelled delete control per character', () => {
      const wrapper = mountList({
        characters: [makeCharacter({ id: 'a', name: 'Alpha' }), makeCharacter({ id: 'b', name: 'Beta' })],
      })

      expect(wrapper.find('button[aria-label="Delete Alpha"]').exists()).toBe(true)
      expect(wrapper.find('button[aria-label="Delete Beta"]').exists()).toBe(true)
    })

    it('calls the store delete action after the user confirms', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true)
      const store = useCharacterStore()
      const deleteSpy = vi.spyOn(store, 'deleteCharacter').mockResolvedValue()

      const wrapper = mountList({ characters: [makeCharacter({ id: 'a', name: 'Alpha' })] })
      await wrapper.find('button[aria-label="Delete Alpha"]').trigger('click')

      expect(window.confirm).toHaveBeenCalledWith('Delete "Alpha"? This cannot be undone.')
      expect(deleteSpy).toHaveBeenCalledWith('a')
    })

    it('does not call the store delete action when the user cancels the confirm', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(false)
      const store = useCharacterStore()
      const deleteSpy = vi.spyOn(store, 'deleteCharacter').mockResolvedValue()

      const wrapper = mountList({ characters: [makeCharacter({ id: 'a', name: 'Alpha' })] })
      await wrapper.find('button[aria-label="Delete Alpha"]').trigger('click')

      expect(deleteSpy).not.toHaveBeenCalled()
    })

    it('removes the character from the displayed list once the store deletes it', async () => {
      vi.spyOn(window, 'confirm').mockReturnValue(true)
      const store = useCharacterStore()
      store.characters = [
        makeCharacter({ id: 'a', name: 'Alpha' }),
        makeCharacter({ id: 'b', name: 'Beta' }),
      ]
      // Falls back to store.characters (no characters prop), so the list reflects store mutations.
      // The real store action runs and calls the mocked api.deleteCharacter (no network).
      const wrapper = mountList()
      expect(wrapper.findAll('.character-card').length).toBe(2)

      await wrapper.find('button[aria-label="Delete Alpha"]').trigger('click')
      await flushPromises()

      expect(store.characters.map((c) => c.id)).toEqual(['b'])
      expect(wrapper.text()).not.toContain('Alpha')
      expect(wrapper.text()).toContain('Beta')
    })
  })
})

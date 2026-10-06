import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import CharacterList from '../CharacterList.vue'
import { useCharacterStore } from '../../store'
import type { CharacterDto } from '../../api'

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
})

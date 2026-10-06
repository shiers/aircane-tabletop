import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import CharacterLibraryView from '../CharacterLibraryView.vue'
import { useCharacterStore } from '../store'
import vBgAsset from '@/directives/vBgAsset'
import type { CharacterDto } from '../api'

// ── Mock the characters api (used through the store) ──────────────────────────
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    listAllCharacters: vi.fn(),
    setCharacterCampaign: vi.fn(),
  }
})

// ── Mock the campaigns api ────────────────────────────────────────────────────
vi.mock('@/features/campaigns/api', () => ({
  listCampaigns: vi.fn(),
}))

import * as charactersApi from '../api'
import * as campaignsApi from '@/features/campaigns/api'

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

const campaigns = [
  { id: 'camp-1', name: 'Lost Mines' },
  { id: 'camp-2', name: 'Dragon Heist' },
]

// Capture the characters passed to CharacterList so filter behaviour can be asserted.
let lastCharacterListProps: { characters?: CharacterDto[]; showCampaignLabel?: boolean } | null = null

const CharacterListStub = {
  props: ['characters', 'showCampaignLabel'],
  setup(props: { characters?: CharacterDto[]; showCampaignLabel?: boolean }) {
    lastCharacterListProps = props
    return () => null
  },
}

function mountView() {
  return mount(CharacterLibraryView, {
    global: {
      directives: { 'bg-asset': vBgAsset },
      stubs: {
        CharacterList: CharacterListStub,
        CharacterForm: true,
        ImportCharacterModal: true,
      },
    },
  })
}

describe('CharacterLibraryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    lastCharacterListProps = null
    vi.mocked(campaignsApi.listCampaigns).mockResolvedValue(campaigns as never)
  })

  it('fetches all characters without a campaignId on mount', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([makeCharacter()])
    mountView()
    await flushPromises()

    expect(charactersApi.listAllCharacters).toHaveBeenCalledTimes(1)
    expect(charactersApi.listAllCharacters).toHaveBeenCalledWith()
  })

  it('renders all characters with correct labels, including Unassigned', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([
      makeCharacter({ id: 'a', name: 'Alpha', campaignId: 'camp-1', campaignName: 'Lost Mines' }),
      makeCharacter({ id: 'b', name: 'Beta', campaignName: null }),
      // Orphan: has a campaignId that no longer resolves => campaignName null => Unassigned.
      makeCharacter({ id: 'c', name: 'Gamma', campaignId: 'ghost', campaignName: null }),
    ])
    mountView()
    await flushPromises()

    expect(lastCharacterListProps?.showCampaignLabel).toBe(true)
    expect(lastCharacterListProps?.characters?.map((c) => c.name)).toEqual(['Alpha', 'Beta', 'Gamma'])
  })

  it('narrows the list with the campaign filter', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([
      makeCharacter({ id: 'a', name: 'Alpha', campaignId: 'camp-1', campaignName: 'Lost Mines' }),
      makeCharacter({ id: 'b', name: 'Beta', campaignName: null }),
      makeCharacter({ id: 'c', name: 'Gamma', campaignId: 'ghost', campaignName: null }),
    ])
    const wrapper = mountView()
    await flushPromises()

    const filter = wrapper.find('#campaign-filter')

    // Unassigned => characters with null campaignName (Beta + orphan Gamma).
    await filter.setValue('unassigned')
    expect(lastCharacterListProps?.characters?.map((c) => c.name)).toEqual(['Beta', 'Gamma'])

    // A specific campaign => only characters assigned to it.
    await filter.setValue('camp-1')
    expect(lastCharacterListProps?.characters?.map((c) => c.name)).toEqual(['Alpha'])

    // All => everything.
    await filter.setValue('all')
    expect(lastCharacterListProps?.characters?.length).toBe(3)
  })

  it('assigns a character via setCharacterCampaign and updates its label', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([
      makeCharacter({ id: 'a', name: 'Alpha', campaignName: null }),
    ])
    vi.mocked(charactersApi.setCharacterCampaign).mockResolvedValue(
      makeCharacter({ id: 'a', name: 'Alpha', campaignId: 'camp-1', campaignName: 'Lost Mines' }),
    )
    const wrapper = mountView()
    await flushPromises()

    const select = wrapper.find('#assign-a')
    expect(select.exists()).toBe(true)
    await select.setValue('camp-1')
    await flushPromises()

    expect(charactersApi.setCharacterCampaign).toHaveBeenCalledWith('a', 'camp-1')
    const store = useCharacterStore()
    expect(store.characters[0].campaignName).toBe('Lost Mines')
  })

  it('unassigns a character via setCharacterCampaign with null', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([
      makeCharacter({ id: 'a', name: 'Alpha', campaignId: 'camp-1', campaignName: 'Lost Mines' }),
    ])
    vi.mocked(charactersApi.setCharacterCampaign).mockResolvedValue(
      makeCharacter({ id: 'a', name: 'Alpha', campaignName: null }),
    )
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('#assign-a').setValue('')
    await flushPromises()

    expect(charactersApi.setCharacterCampaign).toHaveBeenCalledWith('a', null)
  })

  it('refreshes the full list after an import completes', async () => {
    vi.mocked(charactersApi.listAllCharacters).mockResolvedValue([makeCharacter()])
    const wrapper = mountView()
    await flushPromises()
    expect(charactersApi.listAllCharacters).toHaveBeenCalledTimes(1)

    // Simulate the import modal emitting 'completed'.
    await wrapper.findComponent({ name: 'ImportCharacterModal' }).vm.$emit('completed')
    await flushPromises()

    expect(charactersApi.listAllCharacters).toHaveBeenCalledTimes(2)
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import GameSystemSelector from '../components/GameSystemSelector.vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'

// Mock the API module
vi.mock('../api', () => ({
  listGameSystems: vi.fn().mockResolvedValue([]),
  getGameSystem: vi.fn(),
  createGameSystem: vi.fn(),
  updateGameSystem: vi.fn(),
  deactivateGameSystem: vi.fn(),
  importGameSystem: vi.fn(),
  exportGameSystem: vi.fn(),
  validateGameSystem: vi.fn(),
  listTemplates: vi.fn(),
}))

const mockDefinitions = [
  {
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
  },
  {
    id: 'def-2',
    identifier: 'pf2e',
    name: 'Pathfinder 2e',
    version: '1.0.0',
    publisher: 'Paizo',
    genre: 'fantasy',
    description: null,
    license: 'user-created',
    isBuiltIn: false,
    isActive: true,
    createdAt: '2024-02-01T00:00:00Z',
    updatedAt: '2024-02-01T00:00:00Z',
  },
]

describe('GameSystemSelector', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  function mountSelector(modelValue: string | null = null) {
    const store = useGameSystemStore()
    store.definitions = [...mockDefinitions]
    return mount(GameSystemSelector, {
      props: { modelValue },
    })
  }

  it('renders the label', () => {
    const wrapper = mountSelector()
    expect(wrapper.text()).toContain('Game System')
  })

  it('renders all available definitions as options', () => {
    const wrapper = mountSelector()
    const options = wrapper.findAll('option')
    // placeholder + 2 definitions
    expect(options).toHaveLength(3)
    expect(options[1].text()).toContain('D&D 5e 2014')
    expect(options[2].text()).toContain('Pathfinder 2e')
  })

  it('shows definition summary when one is selected', () => {
    const wrapper = mountSelector('def-1')
    expect(wrapper.text()).toContain('D&D 5e 2014')
    expect(wrapper.text()).toContain('v1.0.0')
    expect(wrapper.text()).toContain('fantasy')
    expect(wrapper.text()).toContain('Wizards of the Coast')
  })

  it('does not show summary when nothing is selected', () => {
    const wrapper = mountSelector(null)
    // The summary card shows publisher info which doesn't appear in options
    expect(wrapper.text()).not.toContain('Wizards of the Coast')
    expect(wrapper.text()).not.toContain('fantasy')
  })

  it('emits update:modelValue when selection changes', async () => {
    const wrapper = mountSelector(null)
    const select = wrapper.find('select')
    await select.setValue('def-2')

    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual(['def-2'])
  })

  it('emits null when selection is cleared', async () => {
    const wrapper = mountSelector('def-1')
    const select = wrapper.find('select')
    await select.setValue('')

    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual([null])
  })
})

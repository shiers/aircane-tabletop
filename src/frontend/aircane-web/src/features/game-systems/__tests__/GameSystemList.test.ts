import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import GameSystemList from '../components/GameSystemList.vue'
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
    identifier: 'custom-system',
    name: 'Custom System',
    version: '0.1.0',
    publisher: null,
    genre: 'sci-fi',
    description: 'A custom game system.',
    license: 'user-created',
    isBuiltIn: false,
    isActive: true,
    createdAt: '2024-02-01T00:00:00Z',
    updatedAt: '2024-02-01T00:00:00Z',
  },
]

describe('GameSystemList', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  function mountList() {
    const store = useGameSystemStore()
    store.definitions = [...mockDefinitions]
    store.loading = false
    return mount(GameSystemList)
  }

  it('renders the heading', () => {
    const wrapper = mountList()
    expect(wrapper.text()).toContain('Game System Definitions')
  })

  it('renders all definitions', () => {
    const wrapper = mountList()
    expect(wrapper.text()).toContain('D&D 5e 2014')
    expect(wrapper.text()).toContain('Custom System')
  })

  it('shows version and genre', () => {
    const wrapper = mountList()
    expect(wrapper.text()).toContain('v1.0.0')
    expect(wrapper.text()).toContain('fantasy')
  })

  it('shows Built-in badge for built-in definitions', () => {
    const wrapper = mountList()
    expect(wrapper.text()).toContain('Built-in')
  })

  it('shows Deactivate button only for non-built-in definitions', () => {
    const wrapper = mountList()
    const deactivateButtons = wrapper.findAll('button[title="Deactivate"]')
    expect(deactivateButtons).toHaveLength(1) // Only for custom system
  })

  it('emits select when a definition is clicked', async () => {
    const wrapper = mountList()
    const firstItem = wrapper.findAll('button.flex-1')[0]
    await firstItem.trigger('click')

    expect(wrapper.emitted('select')).toBeTruthy()
    expect(wrapper.emitted('select')![0]).toEqual(['def-1'])
  })

  it('emits edit when Edit button is clicked', async () => {
    const wrapper = mountList()
    const editButtons = wrapper.findAll('button[title="Edit"]')
    await editButtons[0].trigger('click')

    expect(wrapper.emitted('edit')).toBeTruthy()
    expect(wrapper.emitted('edit')![0]).toEqual(['def-1'])
  })

  it('emits create when Create New button is clicked', async () => {
    const wrapper = mountList()
    const createButton = wrapper.find('button')
    await createButton.trigger('click')

    expect(wrapper.emitted('create')).toBeTruthy()
  })

  it('shows empty state when no definitions', () => {
    setActivePinia(createPinia())
    const store = useGameSystemStore()
    store.definitions = []
    store.loading = false
    const wrapper = mount(GameSystemList)
    expect(wrapper.text()).toContain('No game system definitions found')
  })

  it('shows loading state', () => {
    setActivePinia(createPinia())
    const store = useGameSystemStore()
    store.loading = true
    const wrapper = mount(GameSystemList)
    expect(wrapper.text()).toContain('Loading definitions')
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ImportCharacterModal from '../ImportCharacterModal.vue'
import type { SourceImportResponse } from '../../api'

// ── Mock the characters api (used indirectly through the store) ────────────────
vi.mock('../../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api')>()
  return {
    ...actual,
    importCharacterFromSource: vi.fn(),
    importCharacterFromDndBeyondUrl: vi.fn(),
  }
})

// ── Mock the game-systems api (list + preview-form) ────────────────────────────
vi.mock('@/features/game-systems/api', () => ({
  listGameSystems: vi.fn().mockResolvedValue([
    { id: 'gs-1', identifier: 'dnd-5e-2014', name: 'D&D 5e (2014)' },
    { id: 'gs-2', identifier: 'pathfinder-2e-remaster', name: 'Pathfinder 2e Remaster' },
  ]),
  previewCharacterForm: vi.fn().mockResolvedValue({ sections: [] }),
}))

import * as charactersApi from '../../api'

function makeResponse(overrides: Partial<SourceImportResponse> = {}): SourceImportResponse {
  return {
    detectedSource: 'PathbuilderTwo',
    confidence: 'high',
    requiresSourceConfirmation: false,
    ruleset: 'Remaster',
    rulesetRequiresConfirmation: false,
    review: {
      characterId: 'char-1',
      reviewRequired: true,
      unmappedFields: [],
      warnings: [],
      gameSystemDefinitionId: 'gs-2',
      mappedFields: { 'identity.name': 'Aragorn' },
      requiresGameSystemSelection: false,
    },
    ...overrides,
  }
}

function mountModal() {
  return mount(ImportCharacterModal, {
    props: { open: true },
    global: {
      stubs: {
        // Avoid loading the real review panel's async form logic in modal tests.
        ImportReviewPanel: { template: '<div data-testid="review-panel-stub" />' },
        Teleport: true,
      },
    },
  })
}

describe('ImportCharacterModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  describe('tab switching', () => {
    it('shows exactly two tabs and defaults to Upload File', () => {
      const wrapper = mountModal()
      const tabs = wrapper.findAll('[role="tab"]')
      expect(tabs.map((t) => t.text())).toEqual(['Upload File', 'D&D Beyond URL'])
      expect(wrapper.find('input[type="file"]').exists()).toBe(true)
    })

    it('switches to the D&D Beyond URL tab', async () => {
      const wrapper = mountModal()
      const urlTab = wrapper.findAll('[role="tab"]')[1]
      await urlTab.trigger('click')
      expect(wrapper.find('#ddb-url').exists()).toBe(true)
      expect(wrapper.text()).toContain('unofficial API')
    })
  })

  describe('badge rendering', () => {
    it('renders the detected-source badge after a file import', async () => {
      // Stay on the Upload File tab (confirmation pending) so the badge is visible.
      vi.mocked(charactersApi.importCharacterFromSource).mockResolvedValue(
        makeResponse({ requiresSourceConfirmation: true, confidence: 'low' }),
      )
      const wrapper = mountModal()

      // Seed JSON content directly on the component state.
      ;(wrapper.vm as unknown as { jsonText: string }).jsonText = '{"build":{}}'
      await wrapper.vm.$nextTick()

      await (wrapper.vm as unknown as { submitFile: () => Promise<void> }).submitFile()
      await flushPromises()

      const badge = wrapper.find('[data-testid="detected-source-badge"]')
      expect(badge.exists()).toBe(true)
      expect(badge.text()).toBe('Pathbuilder 2e')
    })
  })

  describe('source confirmation dropdown', () => {
    it('shows the source dropdown when confirmation is required', async () => {
      vi.mocked(charactersApi.importCharacterFromSource).mockResolvedValue(
        makeResponse({ detectedSource: 'Unknown', requiresSourceConfirmation: true, confidence: 'low' }),
      )
      const wrapper = mountModal()
      ;(wrapper.vm as unknown as { jsonText: string }).jsonText = '{"unknown":true}'
      await (wrapper.vm as unknown as { submitFile: () => Promise<void> }).submitFile()
      await flushPromises()

      const select = wrapper.find('#source-select')
      expect(select.exists()).toBe(true)
      // Auto-detect + 6 explicit sources
      expect(select.findAll('option').length).toBe(7)
    })
  })

  describe('game-system confirmation dropdown', () => {
    it('shows the game-system picker when selection is required', async () => {
      vi.mocked(charactersApi.importCharacterFromSource).mockResolvedValue(
        makeResponse({
          detectedSource: 'Roll20',
          review: {
            characterId: 'char-1',
            reviewRequired: true,
            unmappedFields: [],
            warnings: [],
            requiresGameSystemSelection: true,
            mappedFields: {},
          },
        }),
      )
      const wrapper = mountModal()
      ;(wrapper.vm as unknown as { jsonText: string }).jsonText = '{"schema_version":1}'
      await (wrapper.vm as unknown as { submitFile: () => Promise<void> }).submitFile()
      await flushPromises()

      const select = wrapper.find('#game-system-select')
      expect(select.exists()).toBe(true)
      expect(wrapper.text()).toContain('D&D 5e (2014)')
    })
  })

  describe('D&D Beyond URL submit', () => {
    it('posts the entered URL and shows the review panel', async () => {
      vi.mocked(charactersApi.importCharacterFromDndBeyondUrl).mockResolvedValue(
        makeResponse({ detectedSource: 'DndBeyondApi', ruleset: '2014', rulesetRequiresConfirmation: true }),
      )
      const wrapper = mountModal()

      const urlTab = wrapper.findAll('[role="tab"]')[1]
      await urlTab.trigger('click')
      await wrapper.find('#ddb-url').setValue('https://www.dndbeyond.com/characters/12345')
      await (wrapper.vm as unknown as { submitDndBeyondUrl: () => Promise<void> }).submitDndBeyondUrl()
      await flushPromises()

      expect(charactersApi.importCharacterFromDndBeyondUrl).toHaveBeenCalledWith(
        expect.objectContaining({ characterUrl: 'https://www.dndbeyond.com/characters/12345' }),
      )
      expect(wrapper.find('[data-testid="review-panel-stub"]').exists()).toBe(true)
    })
  })
})

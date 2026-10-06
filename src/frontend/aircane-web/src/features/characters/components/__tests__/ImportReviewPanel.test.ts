import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ImportReviewPanel from '../ImportReviewPanel.vue'
import type { SourceImportResponse } from '../../api'
import type { FormDescriptor } from '@/features/game-systems/types'

// ── Mock the game-systems api so previewCharacterForm returns a known descriptor ──
const descriptor: FormDescriptor = {
  sections: [
    {
      id: 'basics',
      label: 'Basics',
      fields: [
        { id: 'name', type: 'text', label: 'Name', required: true },
        { id: 'level', type: 'number', label: 'Level', required: true, min: 1, max: 20 },
      ],
    },
    {
      id: 'abilities',
      label: 'Abilities',
      fields: [{ id: 'str', type: 'number', label: 'Strength', required: false, min: 1, max: 30 }],
    },
  ],
}

// previewCharacterForm is mocked to return INTEGER wire field types (0=text, 1=number),
// mirroring the real preview-character-form wire shape, so this test also guards the
// renderer's own normalization for a descriptor that reaches it un-normalized.
vi.mock('@/features/game-systems/api', () => ({
  previewCharacterForm: vi.fn().mockResolvedValue({
    sections: [
      {
        id: 'basics',
        label: 'Basics',
        fields: [
          { id: 'name', type: 0, label: 'Name', required: true },
          { id: 'level', type: 1, label: 'Level', required: true, min: 1, max: 20 },
        ],
      },
      {
        id: 'abilities',
        label: 'Abilities',
        fields: [{ id: 'str', type: 1, label: 'Strength', required: false, min: 1, max: 30 }],
      },
    ],
  }),
}))

// ── Mock applyFieldMappings on the characters api ──────────────────────────────
vi.mock('../../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api')>()
  return {
    ...actual,
    applyFieldMappings: vi.fn().mockResolvedValue({ id: 'char-1' }),
  }
})

import * as charactersApi from '../../api'

void descriptor // referenced for shape documentation

function makeResponse(overrides: Partial<SourceImportResponse> = {}): SourceImportResponse {
  return {
    detectedSource: 'DndBeyondApi',
    confidence: 'high',
    requiresSourceConfirmation: false,
    ruleset: '2014',
    rulesetRequiresConfirmation: true,
    review: {
      characterId: 'char-1',
      reviewRequired: true,
      unmappedFields: [],
      warnings: [],
      gameSystemDefinitionId: 'gs-1',
      mappedFields: { 'identity.name': 'Aragorn', level: '5', 'abilities.strength': '18' },
      requiresGameSystemSelection: false,
    },
    ...overrides,
  }
}

function mountPanel(response: SourceImportResponse) {
  return mount(ImportReviewPanel, { props: { response } })
}

describe('ImportReviewPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('seeds the renderer from mappedFields via the bridge table', async () => {
    const wrapper = mountPanel(makeResponse())
    await flushPromises()

    const nameInput = wrapper.find('#field-name').element as HTMLInputElement
    const levelInput = wrapper.find('#field-level').element as HTMLInputElement
    const strInput = wrapper.find('#field-str').element as HTMLInputElement
    expect(nameInput.value).toBe('Aragorn')
    expect(levelInput.value).toBe('5')
    expect(strInput.value).toBe('18')
  })

  it('shows the read-only "Imported from" header bound from detectedSource', async () => {
    const wrapper = mountPanel(makeResponse())
    await flushPromises()
    const badge = wrapper.find('[data-testid="imported-from-badge"]')
    expect(badge.exists()).toBe(true)
    expect(badge.text()).toBe('D&D Beyond')
  })

  it('renders the D&D 5e ruleset dropdown with an amber confirm highlight', async () => {
    const wrapper = mountPanel(makeResponse())
    await flushPromises()
    const select = wrapper.find('#review-ruleset')
    expect(select.exists()).toBe(true)
    expect((select.element as HTMLSelectElement).value).toBe('2014')
    // Amber highlight class present when rulesetRequiresConfirmation is true.
    expect(select.classes().some((c) => c.includes('amber'))).toBe(true)
    expect(wrapper.text()).toContain('Confirm it before saving')
  })

  it('does not show the ruleset dropdown for non-5e rulesets', async () => {
    const wrapper = mountPanel(makeResponse({ ruleset: 'Remaster', rulesetRequiresConfirmation: false }))
    await flushPromises()
    expect(wrapper.find('#review-ruleset').exists()).toBe(false)
  })

  it('confirm builds well-formed FieldMappingEntry[] with sourceFieldName set (MEDIUM-2)', async () => {
    const wrapper = mountPanel(makeResponse())
    await flushPromises()

    await (wrapper.vm as unknown as { confirm: () => Promise<void> }).confirm()
    await flushPromises()

    expect(charactersApi.applyFieldMappings).toHaveBeenCalledTimes(1)
    const [characterId, payload] = vi.mocked(charactersApi.applyFieldMappings).mock.calls[0]
    expect(characterId).toBe('char-1')
    const mappings = (payload as { mappings: Array<{ sourceFieldName: string; canonicalFieldPath: string; value: string }> }).mappings

    // One entry per mapped field; every entry has a non-null, non-empty sourceFieldName.
    expect(mappings.length).toBe(3)
    for (const m of mappings) {
      expect(m.sourceFieldName).toBeTruthy()
      expect(typeof m.sourceFieldName).toBe('string')
    }
    const byPath = Object.fromEntries(mappings.map((m) => [m.canonicalFieldPath, m]))
    expect(byPath['identity.name'].value).toBe('Aragorn')
    expect(byPath['identity.name'].sourceFieldName).toBe('identity.name')
    expect(byPath['level'].value).toBe('5')
    expect(byPath['abilities.strength'].value).toBe('18')
  })

  it('emits confirmed after a successful save', async () => {
    const wrapper = mountPanel(makeResponse())
    await flushPromises()
    await (wrapper.vm as unknown as { confirm: () => Promise<void> }).confirm()
    await flushPromises()
    expect(wrapper.emitted('confirmed')).toBeTruthy()
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import GameSystemEditor from '../components/GameSystemEditor.vue'
import GameSystemDetail from '../components/GameSystemDetail.vue'
import { parseRulesetLabels, rulesetLabelsError, formatRulesetLabels } from '../rulesetLabels'
import * as api from '../api'
import type { GameSystemDefinitionDetail } from '../types'

vi.mock('../api', () => ({
  listGameSystems: vi.fn().mockResolvedValue([]),
  getGameSystem: vi.fn(),
  createGameSystem: vi.fn(),
  updateGameSystem: vi.fn(),
  deactivateGameSystem: vi.fn(),
  importGameSystem: vi.fn(),
  exportGameSystem: vi.fn(),
  validateGameSystem: vi.fn(),
  listTemplates: vi.fn().mockResolvedValue([]),
}))

function detail(overrides: Partial<GameSystemDefinitionDetail> = {}, metadata: Record<string, unknown> = {}): GameSystemDefinitionDetail {
  return {
    id: 'def-1',
    identifier: 'my-system',
    name: 'My System',
    version: '1.0.0',
    publisher: null,
    genre: null,
    description: null,
    license: 'user-created',
    isBuiltIn: false,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    schemaVersion: 1,
    definitionJson: JSON.stringify({ schemaVersion: 1, metadata: { id: 'my-system', name: 'My System', version: '1.0.0', ...metadata } }),
    ...overrides,
  }
}

function sentMetadata(call: unknown[] | undefined): Record<string, unknown> {
  const json = call?.[call.length - 1] as string
  return JSON.parse(json).metadata
}

async function mountEditor(definitionId?: string) {
  const wrapper = mount(GameSystemEditor, {
    props: definitionId ? { definitionId } : {},
    global: { stubs: { DiceConventionPreview: true } },
  })
  await flushPromises()
  return wrapper
}

function rulesetInput(wrapper: Awaited<ReturnType<typeof mountEditor>>) {
  return wrapper.get('#gs-ruleset-labels')
}

function saveButton(wrapper: Awaited<ReturnType<typeof mountEditor>>) {
  return wrapper.findAll('button').find((b) => b.text() === 'Save')!
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  vi.mocked(api.validateGameSystem).mockResolvedValue({ isValid: true, errors: [] })
  vi.mocked(api.updateGameSystem).mockImplementation(async (id) => detail({ id }))
  vi.mocked(api.createGameSystem).mockResolvedValue(detail({ id: 'new-id' }))
})

describe('ruleset label helpers', () => {
  it('splits on commas, trims, drops blanks and case-insensitive repeats', () => {
    expect(parseRulesetLabels(' 2014, , Remaster ,2014, remaster')).toEqual(['2014', 'Remaster'])
    expect(parseRulesetLabels('')).toEqual([])
  })

  it('formats labels back into the field', () => {
    expect(formatRulesetLabels(['2014', 'Revised'])).toBe('2014, Revised')
    expect(formatRulesetLabels(null)).toBe('')
  })

  it('rejects more than 10 labels or a label over 60 characters', () => {
    expect(rulesetLabelsError(Array.from({ length: 10 }, (_, i) => `r${i}`))).toBeNull()
    expect(rulesetLabelsError(Array.from({ length: 11 }, (_, i) => `r${i}`))).toMatch(/at most 10/)
    expect(rulesetLabelsError(['x'.repeat(60)])).toBeNull()
    expect(rulesetLabelsError(['x'.repeat(61)])).toMatch(/60 characters/)
  })
})

describe('GameSystemEditor: Ruleset labels', () => {
  it('shows the field with its help text', async () => {
    const wrapper = await mountEditor()

    expect(wrapper.get('label[for="gs-ruleset-labels"]').text()).toBe('Ruleset labels')
    expect(wrapper.get('#gs-ruleset-labels-help').text()).toContain(
      'How your books for this system are filed, for example 2014. Leave empty if the system has one edition',
    )
  })

  it('loads the definition’s labels as a comma-separated field', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({}, { rulesets: ['2014', 'Revised'] }))

    const wrapper = await mountEditor('def-1')

    expect((rulesetInput(wrapper).element as HTMLInputElement).value).toBe('2014, Revised')
  })

  it('saves edited labels into metadata.rulesets', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({}, { rulesets: ['2014'] }))
    const wrapper = await mountEditor('def-1')

    await rulesetInput(wrapper).setValue('Remaster, Core 2019')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(sentMetadata(vi.mocked(api.updateGameSystem).mock.calls[0]).rulesets).toEqual(['Remaster', 'Core 2019'])
    expect(wrapper.emitted('saved')).toEqual([['def-1']])
  })

  it('leaves metadata.rulesets out when the field is emptied', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({}, { rulesets: ['2014'] }))
    const wrapper = await mountEditor('def-1')

    await rulesetInput(wrapper).setValue('  ')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(sentMetadata(vi.mocked(api.updateGameSystem).mock.calls[0])).not.toHaveProperty('rulesets')
  })

  it('a definition without labels loads an empty field', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail())

    const wrapper = await mountEditor('def-1')

    expect((rulesetInput(wrapper).element as HTMLInputElement).value).toBe('')
  })

  it('creates a definition with labels', async () => {
    const wrapper = await mountEditor()

    await wrapper.get('input[placeholder="e.g. Dungeons & Dragons 5e"]').setValue('Shadowrun')
    await rulesetInput(wrapper).setValue('6e')
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(sentMetadata(vi.mocked(api.createGameSystem).mock.calls[0]).rulesets).toEqual(['6e'])
  })

  it('refuses to save too many labels, saying why', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail())
    const wrapper = await mountEditor('def-1')

    await rulesetInput(wrapper).setValue(Array.from({ length: 11 }, (_, i) => `r${i}`).join(', '))
    await saveButton(wrapper).trigger('click')
    await flushPromises()

    expect(api.updateGameSystem).not.toHaveBeenCalled()
    expect(wrapper.get('[role="alert"]').text()).toMatch(/at most 10 ruleset labels/)
    expect(rulesetInput(wrapper).attributes('aria-invalid')).toBe('true')
  })
})

describe('GameSystemDetail: Ruleset labels', () => {
  it('shows the labels from the definition', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({ rulesets: ['2014', 'Revised'] }))

    const wrapper = mount(GameSystemDetail, { props: { definitionId: 'def-1' } })
    await flushPromises()

    const section = wrapper.get('[data-testid="ruleset-labels"]')
    expect(section.text()).toContain('Ruleset labels')
    expect(section.findAll('span').map((s) => s.text())).toEqual(['2014', 'Revised'])
  })

  it('says when there are none', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({ rulesets: [] }))

    const wrapper = mount(GameSystemDetail, { props: { definitionId: 'def-1' } })
    await flushPromises()

    expect(wrapper.get('[data-testid="ruleset-labels"]').text()).toContain('None: rules lookups search every edition')
  })

  it('falls back to the definition JSON when the response has no rulesets field', async () => {
    vi.mocked(api.getGameSystem).mockResolvedValue(detail({ rulesets: undefined }, { rulesets: ['Remaster'] }))

    const wrapper = mount(GameSystemDetail, { props: { definitionId: 'def-1' } })
    await flushPromises()

    expect(wrapper.get('[data-testid="ruleset-labels"]').findAll('span').map((s) => s.text())).toEqual(['Remaster'])
  })
})

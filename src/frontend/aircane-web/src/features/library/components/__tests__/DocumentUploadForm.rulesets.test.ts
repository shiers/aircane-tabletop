import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DocumentUploadForm from '../DocumentUploadForm.vue'
import { useLibraryStore } from '../../store'
import { useGameSystemStore } from '@/features/game-systems/stores/useGameSystemStore'
import { ContentVisibility, ImportStatus, SourceType, type SourceDocumentDto } from '../../api'

function document_(overrides: Partial<SourceDocumentDto> = {}): SourceDocumentDto {
  return {
    id: 'doc-1',
    title: 'Core Rules',
    originalFileName: 'core.pdf',
    sourceType: SourceType.Rules,
    gameSystem: 'D&D 5e',
    ruleset: '2014',
    visibility: ContentVisibility.Public,
    importStatus: ImportStatus.Completed,
    isSourceAvailable: true,
    watchedFolderId: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    isBuiltIn: false,
    isDisabled: false,
    licenseKey: null,
    licenseDisplayName: null,
    ...overrides,
  }
}

function mountForm(documents: SourceDocumentDto[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLibraryStore().documents = documents
  // Pre-load the definitions so the form does not try to fetch them.
  useGameSystemStore().definitions = []
  return mount(DocumentUploadForm, {
    global: { plugins: [pinia], stubs: { Teleport: true }, directives: { 'bg-asset': {} } },
  })
}

const rulesetOptions = (w: ReturnType<typeof mountForm>) => w.findAll('#doc-ruleset-options option').map((o) => o.attributes('value'))

describe('DocumentUploadForm ruleset suggestions', () => {
  it('suggests every known ruleset while no game system is entered', () => {
    const w = mountForm([document_(), document_({ id: 'doc-2', gameSystem: 'Pathfinder 2e', ruleset: 'Remaster' })])

    expect(rulesetOptions(w)).toEqual(['2014', 'Remaster'])
  })

  it('suggests only the rulesets of the entered game system', async () => {
    const w = mountForm([document_(), document_({ id: 'doc-2', gameSystem: 'Pathfinder 2e', ruleset: 'Remaster' })])

    await w.get('#doc-game-system').setValue('pathfinder 2E')

    expect(rulesetOptions(w)).toEqual(['Remaster'])
  })

  it('copes with a document that has a ruleset but no game system: it does not match an entered system', async () => {
    const w = mountForm([document_({ gameSystem: null, ruleset: 'House' }), document_({ id: 'doc-2' })])

    // Used to throw while computing the suggestions, since the null game system was trimmed.
    await w.get('#doc-game-system').setValue('D&D 5e')

    expect(rulesetOptions(w)).toEqual(['2014'])
  })

  it('still suggests that document\'s ruleset while no game system is entered', () => {
    const w = mountForm([document_({ gameSystem: null, ruleset: 'House' })])

    expect(rulesetOptions(w)).toEqual(['House'])
  })
})

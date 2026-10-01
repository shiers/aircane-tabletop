import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import FolderScanReviewModal from '../components/FolderScanReviewModal.vue'
import { useLibraryStore } from '../store'
import {
  ScanCandidateFlag,
  SourceType,
  type FolderScanPreviewDto,
} from '../api'

function makePreview(): FolderScanPreviewDto {
  return {
    folderId: 'folder-1',
    filesFound: 3,
    analyzedAt: '2026-01-01T00:00:00Z',
    candidates: [
      {
        sourcePath: 'C:/books/PHB (BnW OCR).pdf',
        fileName: 'PHB (BnW OCR).pdf',
        suggestedTitle: 'PHB',
        suggestedRuleset: null,
        sizeBytes: 1000,
        dedupKey: 'phb',
        flags: [ScanCandidateFlag.DuplicateVariant],
        reason: 'Looks like another copy/variant of the same title in this folder.',
        alreadyImported: false,
      },
      {
        sourcePath: 'C:/books/PHB (Color OCR).pdf',
        fileName: 'PHB (Color OCR).pdf',
        suggestedTitle: 'PHB',
        suggestedRuleset: null,
        sizeBytes: 2000,
        dedupKey: 'phb',
        flags: [ScanCandidateFlag.DuplicateVariant],
        reason: 'Looks like another copy/variant of the same title in this folder.',
        alreadyImported: false,
      },
      {
        sourcePath: 'C:/books/Monster Manual.pdf',
        fileName: 'Monster Manual.pdf',
        suggestedTitle: 'Monster Manual',
        suggestedRuleset: '2014',
        sizeBytes: 3000,
        dedupKey: 'monster manual',
        flags: [],
        reason: null,
        alreadyImported: false,
      },
    ],
  }
}

function mountModal(preview: FolderScanPreviewDto | null = makePreview()) {
  return mount(FolderScanReviewModal, {
    props: {
      open: true,
      folderId: 'folder-1',
      folderName: 'My Books',
      preview,
    },
    global: {
      stubs: { Teleport: true },
    },
  })
}

describe('FolderScanReviewModal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('pre-selects nothing when a preview loads (user always picks)', () => {
    const wrapper = mountModal()
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes).toHaveLength(3)
    expect(checkboxes.every((c) => !(c.element as HTMLInputElement).checked)).toBe(true)
    // Import button disabled with nothing selected.
    const importBtn = wrapper.find('button[type="button"].bg-aircane-600')
    expect(importBtn.attributes('disabled')).toBeDefined()
  })

  it('renders a duplicate-group hint for variants sharing a dedup key', () => {
    const wrapper = mountModal()
    expect(wrapper.text()).toContain('2 possible copies of the same title')
  })

  it('select all checks every candidate and enables import', async () => {
    const wrapper = mountModal()
    await wrapper.get('button.text-aircane-400').trigger('click')
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes.every((c) => (c.element as HTMLInputElement).checked)).toBe(true)
    expect(wrapper.text()).toContain('3 of 3 selected')
  })

  it('sends only selected files with overrides to the store on confirm', async () => {
    const wrapper = mountModal()
    const store = useLibraryStore()
    const importSpy = vi
      .spyOn(store, 'importFolderSelection')
      .mockResolvedValue({
        folderId: 'folder-1',
        filesFound: 3,
        newFiles: 1,
        updatedFiles: 0,
        skippedFiles: 2,
        scannedAt: '2026-01-01T00:00:00Z',
      })

    // Select only the third candidate (Monster Manual).
    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    await checkboxes[2].setValue(true)

    await wrapper.get('button.bg-aircane-600').trigger('click')
    await flushPromises()

    expect(importSpy).toHaveBeenCalledTimes(1)
    const [folderId, items] = importSpy.mock.calls[0]
    expect(folderId).toBe('folder-1')
    // All three files are reported, but only Monster Manual is marked import.
    expect(items).toHaveLength(3)
    const mm = items.find((i) => i.sourcePath === 'C:/books/Monster Manual.pdf')
    expect(mm?.import).toBe(true)
    expect(mm?.title).toBe('Monster Manual')
    expect(mm?.ruleset).toBe('2014')
    expect(mm?.sourceType).toBe(SourceType.Rules)
    // The unselected duplicates are import:false.
    expect(items.filter((i) => i.import)).toHaveLength(1)
  })

  it('shows an empty state when no importable files are found', () => {
    const wrapper = mountModal({
      folderId: 'folder-1',
      filesFound: 0,
      analyzedAt: '2026-01-01T00:00:00Z',
      candidates: [],
    })
    expect(wrapper.text()).toContain('No importable files found')
  })

  it('shows an analyzing state while the preview is null', () => {
    const wrapper = mountModal(null)
    expect(wrapper.text()).toContain('Analyzing folder…')
  })
})

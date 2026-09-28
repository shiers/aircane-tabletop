import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { ScanCandidateFlag, type FolderScanPreviewDto } from '../api'

// Mock the api module so no HTTP happens and we can assert the import payload.
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return {
    ...actual,
    previewFolderScan: vi.fn(),
    importFolderSelection: vi.fn(),
    listDocuments: vi.fn().mockResolvedValue([]),
    getFolders: vi.fn().mockResolvedValue([]),
  }
})

import { previewFolderScan, importFolderSelection } from '../api'
import { useLibraryStore } from '../store'

const preview: FolderScanPreviewDto = {
  folderId: 'folder-1',
  filesFound: 3,
  analyzedAt: '2026-01-01T00:00:00Z',
  candidates: [
    {
      sourcePath: 'C:/b/clean.pdf',
      fileName: 'clean.pdf',
      suggestedTitle: 'Clean',
      suggestedRuleset: '2014',
      sizeBytes: 1,
      dedupKey: 'clean',
      flags: [], // clean → should import
      reason: null,
      alreadyImported: false,
    },
    {
      sourcePath: 'C:/b/dupe.pdf',
      fileName: 'dupe.pdf',
      suggestedTitle: 'Dupe',
      suggestedRuleset: null,
      sizeBytes: 1,
      dedupKey: 'dupe',
      flags: [ScanCandidateFlag.DuplicateVariant], // flagged → should skip
      reason: 'dup',
      alreadyImported: false,
    },
    {
      sourcePath: 'C:/b/maps.pdf',
      fileName: 'maps.pdf',
      suggestedTitle: 'Maps',
      suggestedRuleset: null,
      sizeBytes: 1,
      dedupKey: 'maps',
      flags: [ScanCandidateFlag.LikelyNotRules], // flagged → should skip
      reason: 'asset',
      alreadyImported: false,
    },
  ],
}

describe('quickImportFolder', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(previewFolderScan).mockResolvedValue(preview)
    vi.mocked(importFolderSelection).mockResolvedValue({
      folderId: 'folder-1',
      filesFound: 3,
      newFiles: 1,
      updatedFiles: 0,
      skippedFiles: 2,
      scannedAt: '2026-01-01T00:00:00Z',
    })
  })

  it('imports only unflagged candidates and reports skipped flagged count', async () => {
    const store = useLibraryStore()

    const { result, skippedFlagged } = await store.quickImportFolder('folder-1')

    expect(previewFolderScan).toHaveBeenCalledWith('folder-1')
    expect(importFolderSelection).toHaveBeenCalledTimes(1)

    const [, items] = vi.mocked(importFolderSelection).mock.calls[0]
    // Clean file marked import; both flagged files marked skip.
    expect(items.find((i) => i.sourcePath === 'C:/b/clean.pdf')?.import).toBe(true)
    expect(items.find((i) => i.sourcePath === 'C:/b/dupe.pdf')?.import).toBe(false)
    expect(items.find((i) => i.sourcePath === 'C:/b/maps.pdf')?.import).toBe(false)

    // Clean file carries its suggested title/ruleset.
    const clean = items.find((i) => i.sourcePath === 'C:/b/clean.pdf')
    expect(clean?.title).toBe('Clean')
    expect(clean?.ruleset).toBe('2014')

    expect(skippedFlagged).toBe(2)
    expect(result.newFiles).toBe(1)
  })
})

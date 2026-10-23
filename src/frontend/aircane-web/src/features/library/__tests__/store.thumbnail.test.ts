import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useLibraryStore } from '../store'
import { ContentVisibility, ImportStatus, SourceType, serverMessage, type SourceDocumentDto } from '../api'
import * as api from '../api'

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  setDocumentThumbnail: vi.fn(),
  importFolderSelection: vi.fn(),
  listDocuments: vi.fn().mockResolvedValue([]),
}))

const setThumbnail = vi.mocked(api.setDocumentThumbnail)

const doc = (over: Partial<SourceDocumentDto> = {}): SourceDocumentDto => ({
  id: 'd1', title: 'Book', originalFileName: 'book.pdf', sourceType: SourceType.Rules, gameSystem: null, ruleset: null,
  visibility: ContentVisibility.DMOnly, importStatus: ImportStatus.Completed, isSourceAvailable: true, watchedFolderId: null,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', isBuiltIn: false, isDisabled: false,
  licenseKey: null, licenseDisplayName: null, thumbnailKey: null, ...over,
})

const httpError = (data: unknown, message = 'Request failed with status code 400') =>
  Object.assign(new Error(message), { response: { status: 400, data } })

describe('library store: setDocumentThumbnail', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setThumbnail.mockReset()
  })

  it('updates the document immediately, then keeps the server value', async () => {
    const store = useLibraryStore()
    store.documents = [doc()]
    let resolve!: (d: SourceDocumentDto) => void
    setThumbnail.mockReturnValue(new Promise((r) => { resolve = r }))

    const pending = store.setDocumentThumbnail('d1', 'space-opera-sw')
    expect(store.documents[0].thumbnailKey).toBe('space-opera-sw')   // before the server answers

    resolve(doc({ thumbnailKey: 'space-opera-sw' }))
    await pending
    expect(store.documents[0].thumbnailKey).toBe('space-opera-sw')
  })

  it('clears with null', async () => {
    const store = useLibraryStore()
    store.documents = [doc({ thumbnailKey: 'generic' })]
    setThumbnail.mockResolvedValue(doc({ thumbnailKey: null }))

    await store.setDocumentThumbnail('d1', null)

    expect(setThumbnail).toHaveBeenCalledWith('d1', null)
    expect(store.documents[0].thumbnailKey).toBeNull()
  })

  it('rolls back to the previous value and throws the server message when the request fails', async () => {
    const store = useLibraryStore()
    store.documents = [doc({ thumbnailKey: 'generic' })]
    setThumbnail.mockRejectedValue(httpError({ title: 'Invalid thumbnail key', detail: 'Thumbnail key must be 1-64 characters.' }))

    await expect(store.setDocumentThumbnail('d1', 'space-opera-sw')).rejects.toThrow('Thumbnail key must be 1-64 characters.')

    expect(store.documents[0].thumbnailKey).toBe('generic')
  })

  it('rolls back from automatic to automatic', async () => {
    const store = useLibraryStore()
    store.documents = [doc({ thumbnailKey: undefined })]
    setThumbnail.mockRejectedValue(httpError({ detail: 'nope' }))

    await expect(store.setDocumentThumbnail('d1', 'generic')).rejects.toThrow('nope')

    expect(store.documents[0].thumbnailKey).toBeNull()
  })

  it('does not undo a newer change when an older request fails', async () => {
    const store = useLibraryStore()
    store.documents = [doc()]
    let reject!: (e: unknown) => void
    setThumbnail.mockReturnValueOnce(new Promise((_, r) => { reject = r }))

    const first = store.setDocumentThumbnail('d1', 'space-opera-sw')
    store.documents[0].thumbnailKey = 'cosmic-horror-cc'   // a later choice lands in the meantime
    reject(httpError({ detail: 'late failure' }))
    await expect(first).rejects.toThrow('late failure')

    expect(store.documents[0].thumbnailKey).toBe('cosmic-horror-cc')
  })

  it('ignores an unknown document id without calling the server', async () => {
    const store = useLibraryStore()
    store.documents = [doc()]
    await store.setDocumentThumbnail('missing', 'generic')
    expect(setThumbnail).not.toHaveBeenCalled()
  })
})

describe('serverMessage', () => {
  it('prefers the ProblemDetails detail, then error, then title', () => {
    expect(serverMessage(httpError({ detail: 'D', title: 'T' }))).toBe('D')
    expect(serverMessage(httpError({ error: 'E', title: 'T' }))).toBe('E')
    expect(serverMessage(httpError({ title: 'T' }))).toBe('T')
  })

  it('falls back to the error message, then a generic one', () => {
    expect(serverMessage(httpError('<html>', 'Network Error'))).toBe('Network Error')
    expect(serverMessage(new Error('boom'))).toBe('boom')
    expect(serverMessage('???')).toBe('An unexpected error occurred.')
    expect(serverMessage(null)).toBe('An unexpected error occurred.')
  })
})

describe('library store: importFolderSelection', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(api.importFolderSelection).mockReset()
    vi.mocked(api.importFolderSelection).mockResolvedValue({
      folderId: 'f', filesFound: 0, newFiles: 0, updatedFiles: 0, skippedFiles: 0, scannedAt: '2026-01-01T00:00:00Z',
    })
  })

  it('passes the batch default thumbnail key to the API', async () => {
    await useLibraryStore().importFolderSelection('f', [], 'fantasy-knight-pf')
    expect(api.importFolderSelection).toHaveBeenCalledWith('f', [], 'fantasy-knight-pf')
  })
})

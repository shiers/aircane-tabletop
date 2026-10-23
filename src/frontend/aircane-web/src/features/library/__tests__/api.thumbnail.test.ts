import { beforeEach, describe, expect, it, vi } from 'vitest'
import apiClient from '@/shared/api/client'
import { importFolderSelection, setDocumentThumbnail, SourceType, uploadDocument } from '../api'

vi.mock('@/shared/api/client', () => ({ default: { post: vi.fn(), patch: vi.fn(), get: vi.fn() } }))

const post = vi.mocked(apiClient.post)
const patch = vi.mocked(apiClient.patch)

const file = new File(['%PDF'], 'a.pdf', { type: 'application/pdf' })

describe('library API thumbnail requests', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    post.mockResolvedValue({ data: {} })
    patch.mockResolvedValue({ data: {} })
  })

  it('upload sends thumbnailKey as a form field when one is chosen', async () => {
    await uploadDocument({ file, title: 'T', sourceType: SourceType.Rules, thumbnailKey: 'fantasy-knight-pf' })
    const form = post.mock.calls[0][1] as FormData
    expect(form.get('thumbnailKey')).toBe('fantasy-knight-pf')
  })

  it('upload sends no thumbnailKey field for Automatic', async () => {
    await uploadDocument({ file, title: 'T', sourceType: SourceType.Rules })
    const form = post.mock.calls[0][1] as FormData
    expect(form.has('thumbnailKey')).toBe(false)
  })

  it('PATCHes the thumbnail endpoint with the key, or null for Automatic', async () => {
    await setDocumentThumbnail('abc', 'space-opera-sw')
    await setDocumentThumbnail('abc', null)
    expect(patch).toHaveBeenNthCalledWith(1, '/api/library/documents/abc/thumbnail', { thumbnailKey: 'space-opera-sw' })
    expect(patch).toHaveBeenNthCalledWith(2, '/api/library/documents/abc/thumbnail', { thumbnailKey: null })
  })

  it('folder import sends defaultThumbnailKey only when there is one', async () => {
    const items = [{ sourcePath: 'x', import: true, thumbnailKey: 'generic' }]
    await importFolderSelection('f1', items, 'fantasy-knight-pf')
    await importFolderSelection('f1', items)
    expect(post.mock.calls[0][1]).toEqual({ items, defaultThumbnailKey: 'fantasy-knight-pf' })
    expect(post.mock.calls[1][1]).toEqual({ items })
  })
})

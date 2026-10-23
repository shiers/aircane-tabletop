import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import FolderScanReviewModal from '../components/FolderScanReviewModal.vue'
import { useLibraryStore } from '../store'
import { SourceType, type FolderScanPreviewDto, type WatchedFolderDto } from '../api'

const candidate = (name: string, ruleset: string | null = null) => ({
  sourcePath: `C:/books/${name}.pdf`,
  fileName: `${name}.pdf`,
  suggestedTitle: name,
  suggestedRuleset: ruleset,
  sizeBytes: 1000,
  dedupKey: name.toLowerCase(),
  flags: [],
  reason: null,
  alreadyImported: false,
})

const preview: FolderScanPreviewDto = {
  folderId: 'folder-1',
  filesFound: 3,
  analyzedAt: '2026-01-01T00:00:00Z',
  candidates: [candidate('Alpha'), candidate('Beta'), candidate('Gamma')],
}

const result = { folderId: 'folder-1', filesFound: 3, newFiles: 3, updatedFiles: 0, skippedFiles: 0, scannedAt: '2026-01-01T00:00:00Z' }

function mountModal(defaultGameSystem: string | null = 'D&D 5e'): VueWrapper {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLibraryStore().folders = [{ id: 'folder-1', defaultGameSystem } as WatchedFolderDto]
  return mount(FolderScanReviewModal, {
    props: { open: true, folderId: 'folder-1', folderName: 'My Books', preview },
    global: { plugins: [pinia], stubs: { Teleport: true } },
    attachTo: document.body,
  })
}

const dialog = (w: VueWrapper) => w.find('[role="dialog"][aria-labelledby="thumbnail-picker-title"]')
const batch = (w: VueWrapper) => w.get('[data-test="batch-thumbnail"]')
const rowButtons = (w: VueWrapper) => w.findAll('[data-action="open-thumbnail-picker"]').filter((b) => b.classes().includes('w-11'))

async function selectAll(w: VueWrapper): Promise<void> {
  await w.get('button.text-aircane-400').trigger('click')
}

async function choose(w: VueWrapper, opener: ReturnType<VueWrapper['get']>, key: string | ''): Promise<void> {
  await opener.trigger('click')
  await w.get(`[data-option="${key}"]`).trigger('click')
  await w.get('[data-action="apply"]').trigger('click')
}

describe('FolderScanReviewModal thumbnails', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('offers a batch thumbnail, defaulting to Automatic ("chosen for each file")', () => {
    const w = mountModal()
    expect(batch(w).text()).toContain('Applies to all files in this import')
    expect(batch(w).get('[data-test="thumbnail-caption"]').text()).toBe('Automatic (chosen for each file)')
    expect(batch(w).find('[data-action="reset-thumbnail"]').exists()).toBe(false)
    w.unmount()
  })

  it('only shows a per-file thumbnail button on files selected for import', async () => {
    const w = mountModal()
    expect(rowButtons(w)).toHaveLength(0)
    await w.findAll('input[type="checkbox"]')[1].setValue(true)
    expect(rowButtons(w)).toHaveLength(1)
    w.unmount()
  })

  it('each selected row shows the thumbnail it will get (the derived one by default)', async () => {
    const w = mountModal('Alien')
    await selectAll(w)
    for (const img of w.findAll('[data-action="open-thumbnail-picker"].w-11 img')) {
      expect(img.attributes('src')).toBe('/assets/document-thumbnails/scifi-horror-al.webp')
    }
    expect(rowButtons(w)).toHaveLength(3)
    w.unmount()
  })

  it('updates a row\'s derived thumbnail when its type changes', async () => {
    const w = mountModal(null)
    await w.findAll('input[type="checkbox"]')[0].setValue(true)
    expect(rowButtons(w)[0].get('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-rulebook-dd.webp')

    await w.get('select').setValue(SourceType.Homebrew)

    expect(rowButtons(w)[0].get('img').attributes('src')).toBe('/assets/document-thumbnails/homebrew.webp')
    w.unmount()
  })

  it('the batch choice shows on every file and is sent as the default, with no per-file keys', async () => {
    const w = mountModal()
    const importSpy = vi.spyOn(useLibraryStore(), 'importFolderSelection').mockResolvedValue(result)
    await selectAll(w)

    await choose(w, batch(w).get('[data-action="open-thumbnail-picker"]'), 'fantasy-knight-pf')

    expect(batch(w).get('[data-test="thumbnail-caption"]').text()).toBe('Fantasy knight')
    for (const b of rowButtons(w)) {
      expect(b.get('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    }

    await w.get('button.bg-aircane-600').trigger('click')
    await flushPromises()

    expect(importSpy).toHaveBeenCalledTimes(1)
    const [folderId, items, defaultKey] = importSpy.mock.calls[0]
    expect(folderId).toBe('folder-1')
    expect(defaultKey).toBe('fantasy-knight-pf')
    expect(items.every((i) => !('thumbnailKey' in i))).toBe(true)
    w.unmount()
  })

  it('a per-file override wins over the batch default and is sent on that file only', async () => {
    const w = mountModal()
    const importSpy = vi.spyOn(useLibraryStore(), 'importFolderSelection').mockResolvedValue(result)
    await selectAll(w)
    await choose(w, batch(w).get('[data-action="open-thumbnail-picker"]'), 'fantasy-knight-pf')

    await choose(w, rowButtons(w)[1], 'space-opera-sw')

    const srcs = rowButtons(w).map((b) => b.get('img').attributes('src'))
    expect(srcs).toEqual([
      '/assets/document-thumbnails/fantasy-knight-pf.webp',
      '/assets/document-thumbnails/space-opera-sw.webp',
      '/assets/document-thumbnails/fantasy-knight-pf.webp',
    ])

    await w.get('button.bg-aircane-600').trigger('click')
    await flushPromises()

    const [, items, defaultKey] = importSpy.mock.calls[0]
    expect(defaultKey).toBe('fantasy-knight-pf')
    expect(items.find((i) => i.sourcePath.endsWith('Beta.pdf'))?.thumbnailKey).toBe('space-opera-sw')
    expect(items.find((i) => i.sourcePath.endsWith('Alpha.pdf'))).not.toHaveProperty('thumbnailKey')
    expect(items.find((i) => i.sourcePath.endsWith('Gamma.pdf'))).not.toHaveProperty('thumbnailKey')
    w.unmount()
  })

  it('works with a per-file choice and no batch default', async () => {
    const w = mountModal()
    const importSpy = vi.spyOn(useLibraryStore(), 'importFolderSelection').mockResolvedValue(result)
    await w.findAll('input[type="checkbox"]')[2].setValue(true)

    await choose(w, rowButtons(w)[0], 'cosmic-horror-cc')
    await w.get('button.bg-aircane-600').trigger('click')
    await flushPromises()

    // Called with two arguments: there is no batch default to send.
    expect(importSpy.mock.calls[0]).toHaveLength(2)
    expect(importSpy.mock.calls[0][1].find((i) => i.sourcePath.endsWith('Gamma.pdf'))?.thumbnailKey).toBe('cosmic-horror-cc')
    w.unmount()
  })

  it('sends neither a default nor per-file keys when nothing was chosen', async () => {
    const w = mountModal()
    const importSpy = vi.spyOn(useLibraryStore(), 'importFolderSelection').mockResolvedValue(result)
    await selectAll(w)

    await w.get('button.bg-aircane-600').trigger('click')
    await flushPromises()

    expect(importSpy.mock.calls[0]).toHaveLength(2)
    expect(importSpy.mock.calls[0][1].every((i) => !('thumbnailKey' in i))).toBe(true)
    w.unmount()
  })

  it('Automatic on a row goes back to the batch choice, and the row picker says so', async () => {
    const w = mountModal()
    await selectAll(w)
    await choose(w, batch(w).get('[data-action="open-thumbnail-picker"]'), 'fantasy-knight-pf')
    await choose(w, rowButtons(w)[0], 'space-opera-sw')

    await rowButtons(w)[0].trigger('click')
    expect(w.get('[role="radio"][data-option=""]').text()).toContain('Same as the batch')
    await w.get('[data-option=""]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')

    expect(rowButtons(w)[0].get('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    w.unmount()
  })

  it('the batch picker\'s Automatic tile shows an ornament, not a picture', async () => {
    const w = mountModal()
    await batch(w).get('[data-action="open-thumbnail-picker"]').trigger('click')
    expect(dialog(w).exists()).toBe(true)
    expect(w.get('[role="radio"][data-option=""]').find('img').exists()).toBe(false)
    w.unmount()
  })

  it('forgets the batch choice when a new preview arrives', async () => {
    const w = mountModal()
    await choose(w, batch(w).get('[data-action="open-thumbnail-picker"]'), 'fantasy-knight-pf')
    await w.setProps({ preview: { ...preview, candidates: [candidate('Delta')] } })
    expect(batch(w).get('[data-test="thumbnail-caption"]').text()).toBe('Automatic (chosen for each file)')
    w.unmount()
  })

  it('Escape in the thumbnail picker closes only the picker', async () => {
    const w = mountModal()
    await batch(w).get('[data-action="open-thumbnail-picker"]').trigger('click')
    await dialog(w).trigger('keydown', { key: 'Escape' })
    expect(dialog(w).exists()).toBe(false)
    expect(w.emitted('close')).toBeUndefined()
    w.unmount()
  })
})

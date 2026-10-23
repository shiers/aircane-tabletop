import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DocumentList from '../DocumentList.vue'
import { useLibraryStore } from '../../store'
import { ContentVisibility, ImportStatus, SourceType, type SourceDocumentDto } from '../../api'
import * as api from '../../api'

vi.mock('../../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api')>()),
  setDocumentThumbnail: vi.fn(),
}))

const setThumbnail = vi.mocked(api.setDocumentThumbnail)

function makeDoc(overrides: Partial<SourceDocumentDto> = {}): SourceDocumentDto {
  return {
    id: 'doc-1',
    title: 'Bestiary',
    originalFileName: 'bestiary.pdf',
    sourceType: SourceType.Rules,
    gameSystem: 'Pathfinder',
    ruleset: null,
    visibility: ContentVisibility.DMOnly,
    importStatus: ImportStatus.Completed,
    isSourceAvailable: true,
    watchedFolderId: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    isBuiltIn: false,
    isDisabled: false,
    licenseKey: null,
    licenseDisplayName: null,
    thumbnailKey: null,
    ...overrides,
  }
}

function mountList(docs: SourceDocumentDto[]): VueWrapper {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLibraryStore().documents = docs
  return mount(DocumentList, {
    global: { plugins: [pinia], stubs: { Teleport: true }, directives: { 'bg-asset': {} } },
    attachTo: document.body,
  })
}

const rowImgSrc = (w: VueWrapper, index = 0) =>
  w.findAll('[data-test="row-thumbnail"]')[index].find('img').attributes('src')

const dialog = (w: VueWrapper) => w.find('[role="dialog"][aria-labelledby="thumbnail-picker-title"]')

/** An axios-style failure carrying a ProblemDetails body. */
const serverError = (detail: string) =>
  Object.assign(new Error('Request failed with status code 400'), { response: { status: 400, data: { detail } } })

describe('DocumentList thumbnails', () => {
  beforeEach(() => {
    setThumbnail.mockReset()
  })

  // ── What the table shows ────────────────────────────────────────────────

  it('shows the stored thumbnail instead of the derived one', () => {
    const w = mountList([
      makeDoc({ id: 'a', thumbnailKey: 'space-opera-sw' }),   // Pathfinder rules would derive the knight
      makeDoc({ id: 'b', thumbnailKey: null }),
    ])
    expect(rowImgSrc(w, 0)).toBe('/assets/document-thumbnails/space-opera-sw.webp')
    expect(rowImgSrc(w, 1)).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    w.unmount()
  })

  it('ignores a stored key whose image was removed and uses the automatic choice', () => {
    const w = mountList([makeDoc({ thumbnailKey: 'removed-asset' })])
    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    w.unmount()
  })

  it('draws the row thumbnail in a fixed 40 x 53 box inside a 44 px wide button', () => {
    const w = mountList([makeDoc()])
    const box = w.get('[data-test="row-thumbnail"]')
    expect(box.classes()).toEqual(expect.arrayContaining(['h-[53px]', 'w-10']))
    expect(w.get('[data-action="row-change-thumbnail"]').classes()).toEqual(expect.arrayContaining(['h-[53px]', 'w-11']))
    w.unmount()
  })

  // ── Row entry point ─────────────────────────────────────────────────────

  it('opens the picker from the row thumbnail without selecting the row', async () => {
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])

    await w.get('[data-action="row-change-thumbnail"]').trigger('click')

    expect(dialog(w).exists()).toBe(true)
    expect(dialog(w).text()).toContain('Bestiary')
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('data-option')).toBe('space-opera-sw')
    expect(w.find('.document-detail-panel').exists()).toBe(false)
    expect(w.get('[data-action="row-change-thumbnail"]').attributes('aria-label')).toBe('Change thumbnail for Bestiary')
    w.unmount()
  })

  it('shows what Automatic would pick for that document on the Automatic tile', async () => {
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])
    await w.get('[data-action="row-change-thumbnail"]').trigger('click')
    // Stored key is ignored for the Automatic tile: Pathfinder rules derive the knight.
    expect(w.get('[role="radio"][data-option=""] img').attributes('src')).toBe(
      '/assets/document-thumbnails/fantasy-knight-pf.webp',
    )
    w.unmount()
  })

  it('saves the chosen thumbnail and updates the row at once, before the server answers', async () => {
    let resolve!: (d: SourceDocumentDto) => void
    setThumbnail.mockReturnValue(new Promise((r) => { resolve = r }))
    const w = mountList([makeDoc()])

    await w.get('[data-action="row-change-thumbnail"]').trigger('click')
    await w.get('[data-option="gothic-vampire-vm"]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')

    expect(setThumbnail).toHaveBeenCalledWith('doc-1', 'gothic-vampire-vm')
    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/gothic-vampire-vm.webp')   // optimistic
    expect(dialog(w).exists()).toBe(false)

    resolve(makeDoc({ thumbnailKey: 'gothic-vampire-vm' }))
    await flushPromises()
    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/gothic-vampire-vm.webp')
    expect(useLibraryStore().documents[0].thumbnailKey).toBe('gothic-vampire-vm')
    expect(w.find('[role="alert"]').exists()).toBe(false)
    w.unmount()
  })

  it('rolls the row back and shows the server message when the save fails', async () => {
    setThumbnail.mockRejectedValue(serverError('Thumbnail key must be 1-64 characters.'))
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])

    await w.get('[data-action="row-change-thumbnail"]').trigger('click')
    await w.get('[data-option="gothic-vampire-vm"]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
    await flushPromises()

    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/space-opera-sw.webp')
    expect(useLibraryStore().documents[0].thumbnailKey).toBe('space-opera-sw')
    expect(w.get('[role="alert"]').text()).toBe('Thumbnail key must be 1-64 characters.')
    w.unmount()
  })

  it('clears the previous error on the next attempt', async () => {
    setThumbnail.mockRejectedValueOnce(serverError('Nope.')).mockResolvedValueOnce(makeDoc({ thumbnailKey: 'generic' }))
    const w = mountList([makeDoc()])
    const choose = async () => {
      await w.get('[data-action="row-change-thumbnail"]').trigger('click')
      await w.get('[data-option="generic"]').trigger('click')
      await w.get('[data-action="apply"]').trigger('click')
      await flushPromises()
    }

    await choose()
    expect(w.find('[role="alert"]').exists()).toBe(true)
    await choose()
    expect(w.find('[role="alert"]').exists()).toBe(false)
    w.unmount()
  })

  it('cancelling the picker saves nothing', async () => {
    const w = mountList([makeDoc()])
    await w.get('[data-action="row-change-thumbnail"]').trigger('click')
    await w.get('[data-option="generic"]').trigger('click')
    await w.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
    expect(setThumbnail).not.toHaveBeenCalled()
    expect(dialog(w).exists()).toBe(false)
    w.unmount()
  })

  // ── Detail panel entry point ────────────────────────────────────────────

  async function selectRow(w: VueWrapper): Promise<void> {
    await w.get('tr.document-row').trigger('click')
  }

  it('shows a larger preview and a Change thumbnail button in the detail panel', async () => {
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])
    await selectRow(w)

    const preview = w.get('[data-test="detail-thumbnail"]')
    expect(preview.find('img').attributes('src')).toBe('/assets/document-thumbnails/space-opera-sw.webp')
    expect(preview.classes()).toEqual(expect.arrayContaining(['h-[128px]', 'w-24']))
    expect(w.get('[data-action="change-thumbnail"]').text()).toBe('Change thumbnail')
    w.unmount()
  })

  it('opens the picker from the detail panel and saves', async () => {
    setThumbnail.mockResolvedValue(makeDoc({ thumbnailKey: 'crystal-fantasy-dg' }))
    const w = mountList([makeDoc()])
    await selectRow(w)

    await w.get('[data-action="change-thumbnail"]').trigger('click')
    expect(dialog(w).exists()).toBe(true)
    await w.get('[data-option="crystal-fantasy-dg"]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
    await flushPromises()

    expect(setThumbnail).toHaveBeenCalledWith('doc-1', 'crystal-fantasy-dg')
    expect(w.get('[data-test="detail-thumbnail"] img').attributes('src')).toBe('/assets/document-thumbnails/crystal-fantasy-dg.webp')
    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/crystal-fantasy-dg.webp')   // table and panel stay in sync
    w.unmount()
  })

  it('offers "Reset to automatic" only when a thumbnail was chosen, and clears it with null', async () => {
    setThumbnail.mockResolvedValue(makeDoc({ thumbnailKey: null }))
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])
    await selectRow(w)

    await w.get('[data-action="reset-thumbnail"]').trigger('click')
    await flushPromises()

    expect(setThumbnail).toHaveBeenCalledWith('doc-1', null)
    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    expect(w.find('[data-action="reset-thumbnail"]').exists()).toBe(false)   // already automatic
    w.unmount()
  })

  it('does not offer a reset for a document that is already automatic', async () => {
    const w = mountList([makeDoc({ thumbnailKey: null })])
    await selectRow(w)
    expect(w.find('[data-action="reset-thumbnail"]').exists()).toBe(false)
    w.unmount()
  })

  it('rolls back a failed reset too', async () => {
    setThumbnail.mockRejectedValue(serverError('Forbidden.'))
    const w = mountList([makeDoc({ thumbnailKey: 'space-opera-sw' })])
    await selectRow(w)

    await w.get('[data-action="reset-thumbnail"]').trigger('click')
    await flushPromises()

    expect(rowImgSrc(w)).toBe('/assets/document-thumbnails/space-opera-sw.webp')
    expect(w.get('[role="alert"]').text()).toBe('Forbidden.')
    expect(w.find('[data-action="reset-thumbnail"]').exists()).toBe(true)
    w.unmount()
  })

  it('says when the stored thumbnail is no longer available, and the picker shows Automatic selected', async () => {
    const w = mountList([makeDoc({ thumbnailKey: 'removed-asset' })])
    await selectRow(w)

    expect(w.get('.document-detail-panel').text()).toContain('This thumbnail is no longer available.')
    await w.get('[data-action="change-thumbnail"]').trigger('click')
    expect(w.get('[role="status"]').text()).toContain('This thumbnail is no longer available')
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('aria-label')).toBe('Automatic')
    w.unmount()
  })

  it('works on built-in documents too', async () => {
    setThumbnail.mockResolvedValue(makeDoc({ isBuiltIn: true, thumbnailKey: 'cavern-adventure-lm' }))
    const w = mountList([makeDoc({ isBuiltIn: true })])
    await w.get('[data-action="row-change-thumbnail"]').trigger('click')
    await w.get('[data-option="cavern-adventure-lm"]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
    await flushPromises()
    expect(setThumbnail).toHaveBeenCalledWith('doc-1', 'cavern-adventure-lm')
    w.unmount()
  })
})

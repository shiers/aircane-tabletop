import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DocumentUploadForm from '../DocumentUploadForm.vue'
import { useLibraryStore } from '../../store'
import { useGameSystemStore } from '@/features/game-systems/stores/useGameSystemStore'
import { SourceType } from '../../api'

function mountForm(): VueWrapper {
  const pinia = createPinia()
  setActivePinia(pinia)
  // Pre-load the definitions so the form does not try to fetch them.
  useGameSystemStore().definitions = [{ id: 'g', name: 'Pathfinder 2e' } as never]
  return mount(DocumentUploadForm, {
    global: { plugins: [pinia], stubs: { Teleport: true }, directives: { 'bg-asset': {} } },
    attachTo: document.body,
  })
}

async function chooseFile(w: VueWrapper, name = 'bestiary.pdf'): Promise<void> {
  const input = w.get('#doc-file').element as HTMLInputElement
  Object.defineProperty(input, 'files', { value: [new File(['%PDF'], name, { type: 'application/pdf' })], configurable: true })
  await w.get('#doc-file').trigger('change')
}

const section = (w: VueWrapper) => w.find('[data-test="upload-thumbnail-section"]')
const dialog = (w: VueWrapper) => w.find('[role="dialog"]')

describe('DocumentUploadForm thumbnail', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('has no Thumbnail section until a file is chosen', async () => {
    const w = mountForm()
    expect(section(w).exists()).toBe(false)
    await chooseFile(w)
    expect(section(w).exists()).toBe(true)
    expect(section(w).text()).toContain('Thumbnail')
    w.unmount()
  })

  it('defaults to Automatic and shows the thumbnail it will get', async () => {
    const w = mountForm()
    await chooseFile(w)

    expect(section(w).get('[data-test="thumbnail-caption"]').text()).toBe('Automatic (Fantasy rulebook)')
    expect(section(w).get('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-rulebook-dd.webp')
    expect(section(w).find('[data-action="reset-thumbnail"]').exists()).toBe(false)
    w.unmount()
  })

  it('updates what Automatic shows as the source type, system and ruleset are entered', async () => {
    const w = mountForm()
    await chooseFile(w)

    await w.get('#doc-game-system').setValue('Pathfinder 2e')
    expect(section(w).get('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')

    await w.get('#doc-game-system').setValue('Alien')
    expect(section(w).get('img').attributes('src')).toBe('/assets/document-thumbnails/scifi-horror-al.webp')

    await w.get('#doc-game-system').setValue('')
    await w.get('#doc-source-type').setValue(SourceType.Homebrew)
    expect(section(w).get('img').attributes('src')).toBe('/assets/document-thumbnails/homebrew.webp')
    w.unmount()
  })

  it('opens the picker with Automatic showing the same derived thumbnail', async () => {
    const w = mountForm()
    await chooseFile(w)
    await w.get('#doc-game-system').setValue('Shadowrun')

    await section(w).get('[data-action="open-thumbnail-picker"]').trigger('click')

    expect(dialog(w).exists()).toBe(true)
    expect(w.get('[role="radio"][data-option=""] img').attributes('src')).toBe('/assets/document-thumbnails/cyberpunk-fantasy-sr.webp')
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('aria-label')).toBe('Automatic')
    w.unmount()
  })

  async function pick(w: VueWrapper, key: string): Promise<void> {
    await section(w).get('[data-action="open-thumbnail-picker"]').trigger('click')
    await w.get(`[data-option="${key}"]`).trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
  }

  it('shows the chosen thumbnail and sends its key with the upload', async () => {
    const w = mountForm()
    const upload = vi.spyOn(useLibraryStore(), 'uploadDocument').mockResolvedValue()
    await chooseFile(w)
    await w.get('#doc-title').setValue('Bestiary')

    await pick(w, 'space-opera-sw')

    expect(section(w).get('[data-test="thumbnail-caption"]').text()).toBe('Space opera')
    expect(section(w).get('img').attributes('src')).toBe('/assets/document-thumbnails/space-opera-sw.webp')

    await w.get('form').trigger('submit')
    await flushPromises()

    expect(upload).toHaveBeenCalledTimes(1)
    expect(upload.mock.calls[0][0]).toMatchObject({ title: 'Bestiary', thumbnailKey: 'space-opera-sw' })
    w.unmount()
  })

  it('sends no key when the thumbnail was left on Automatic', async () => {
    const w = mountForm()
    const upload = vi.spyOn(useLibraryStore(), 'uploadDocument').mockResolvedValue()
    await chooseFile(w)

    await w.get('form').trigger('submit')
    await flushPromises()

    expect(upload.mock.calls[0][0].thumbnailKey).toBeUndefined()
    w.unmount()
  })

  it('Reset to automatic drops the choice', async () => {
    const w = mountForm()
    const upload = vi.spyOn(useLibraryStore(), 'uploadDocument').mockResolvedValue()
    await chooseFile(w)
    await pick(w, 'space-opera-sw')

    await section(w).get('[data-action="reset-thumbnail"]').trigger('click')

    expect(section(w).get('[data-test="thumbnail-caption"]').text()).toBe('Automatic (Fantasy rulebook)')
    await w.get('form').trigger('submit')
    await flushPromises()
    expect(upload.mock.calls[0][0].thumbnailKey).toBeUndefined()
    w.unmount()
  })

  it('goes back to Automatic after a successful upload', async () => {
    const w = mountForm()
    vi.spyOn(useLibraryStore(), 'uploadDocument').mockResolvedValue()
    await chooseFile(w)
    await pick(w, 'space-opera-sw')

    await w.get('form').trigger('submit')
    await flushPromises()

    expect(section(w).exists()).toBe(false)   // the form reset, so no file is chosen
    await chooseFile(w, 'next.pdf')
    expect(section(w).get('[data-test="thumbnail-caption"]').text()).toContain('Automatic')
    w.unmount()
  })

  it('keeps the choice when the upload fails, so a retry does not lose it', async () => {
    const w = mountForm()
    vi.spyOn(useLibraryStore(), 'uploadDocument').mockRejectedValue(new Error('boom'))
    await chooseFile(w)
    await pick(w, 'space-opera-sw')

    await w.get('form').trigger('submit')
    await flushPromises()

    expect(section(w).get('[data-test="thumbnail-caption"]').text()).toBe('Space opera')
    w.unmount()
  })

  it('has a thumbnail button with a 44 px tap target', async () => {
    const w = mountForm()
    await chooseFile(w)
    const button = section(w).get('[data-action="open-thumbnail-picker"]')
    expect(button.classes()).toEqual(expect.arrayContaining(['min-h-[44px]', 'min-w-[44px]']))
    expect(button.attributes('aria-label')).toBe('Change thumbnail for bestiary')
    w.unmount()
  })
})

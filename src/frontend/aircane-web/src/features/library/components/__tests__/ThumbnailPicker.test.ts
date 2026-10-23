import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ThumbnailPicker from '../ThumbnailPicker.vue'
import { THUMBNAIL_FILTERS, THUMBNAIL_MANIFEST } from '../../thumbnails'

function mountPicker(props: { modelValue?: string | null; autoKey?: string | null; unavailable?: boolean } = {}) {
  const wrapper = mount(ThumbnailPicker, {
    props: {
      modelValue: null,
      autoKey: 'fantasy-rulebook-dd',
      ...props,
      'onUpdate:modelValue': (v: string | null) => wrapper.setProps({ modelValue: v }),
    },
    attachTo: document.body,
  })
  return wrapper
}

const radios = (w: ReturnType<typeof mountPicker>) => w.findAll('[role="radio"]')
const keys = (w: ReturnType<typeof mountPicker>) => radios(w).map((r) => r.attributes('data-option'))

describe('ThumbnailPicker', () => {
  it('shows the Automatic tile first, then every manifest thumbnail with its label', () => {
    const w = mountPicker()
    expect(keys(w)).toEqual(['', ...THUMBNAIL_MANIFEST.map((e) => e.key)])
    expect(radios(w)[0].attributes('aria-label')).toBe('Automatic')
    expect(radios(w)[1].attributes('aria-label')).toBe(THUMBNAIL_MANIFEST[0].label)
    w.unmount()
  })

  it('shows what Automatic would pick right now on the Automatic tile', () => {
    const w = mountPicker({ autoKey: 'fantasy-knight-pf' })
    const auto = radios(w)[0]
    expect(auto.text()).toContain('Fantasy knight')
    expect(auto.find('img').attributes('src')).toBe('/assets/document-thumbnails/fantasy-knight-pf.webp')
    w.unmount()
  })

  it('shows an ornament instead of a picture when Automatic differs per file', () => {
    const w = mountPicker({ autoKey: null })
    const auto = radios(w)[0]
    expect(auto.find('img').exists()).toBe(false)
    expect(auto.text()).toContain('Chosen for each file')
    w.unmount()
  })

  it('marks exactly the selected tile as checked, and Automatic when the value is null', () => {
    const w = mountPicker({ modelValue: 'space-opera-sw' })
    const checked = radios(w).filter((r) => r.attributes('aria-checked') === 'true')
    expect(checked.map((r) => r.attributes('data-option'))).toEqual(['space-opera-sw'])
    w.unmount()

    const auto = mountPicker({ modelValue: null })
    expect(radios(auto)[0].attributes('aria-checked')).toBe('true')
    expect(radios(auto).filter((r) => r.attributes('aria-checked') === 'true')).toHaveLength(1)
    auto.unmount()
  })

  it('selects a thumbnail on click and Automatic on click', async () => {
    const w = mountPicker()
    await w.get('[data-option="gothic-vampire-vm"]').trigger('click')
    expect(w.emitted('update:modelValue')!.at(-1)).toEqual(['gothic-vampire-vm'])
    expect(w.get('[data-option="gothic-vampire-vm"]').attributes('aria-checked')).toBe('true')

    await w.get('[data-option=""]').trigger('click')
    expect(w.emitted('update:modelValue')!.at(-1)).toEqual([null])
    w.unmount()
  })

  it('offers the filter chips in order, with "All" pressed by default', () => {
    const w = mountPicker()
    const chips = w.findAll('[data-filter]')
    expect(chips.map((c) => c.text())).toEqual(['All', 'Fantasy', 'Sci-fi', 'Horror', 'Grimdark', 'Generic', 'Homebrew'])
    expect(chips.map((c) => c.attributes('data-filter'))).toEqual(THUMBNAIL_FILTERS.map((f) => f.id))
    expect(chips[0].attributes('aria-pressed')).toBe('true')
    expect(chips[1].attributes('aria-pressed')).toBe('false')
    w.unmount()
  })

  it.each(THUMBNAIL_FILTERS.filter((f) => f.id !== 'all'))('filters to the "$id" tag', async ({ id }) => {
    const w = mountPicker()
    await w.get(`[data-filter="${id}"]`).trigger('click')
    const expected = THUMBNAIL_MANIFEST.filter((e) => e.tags.includes(id)).map((e) => e.key)
    expect(expected.length).toBeGreaterThan(0)
    // Automatic stays available whatever the filter.
    expect(keys(w)).toEqual(['', ...expected])
    expect(w.get(`[data-filter="${id}"]`).attributes('aria-pressed')).toBe('true')
    w.unmount()
  })

  it('"All" brings every thumbnail back', async () => {
    const w = mountPicker()
    await w.get('[data-filter="horror"]').trigger('click')
    await w.get('[data-filter="all"]').trigger('click')
    expect(radios(w)).toHaveLength(THUMBNAIL_MANIFEST.length + 1)
    w.unmount()
  })

  it('keeps the selection in the tab order even when the filter hides it', async () => {
    const w = mountPicker({ modelValue: 'fantasy-knight-pf' })
    expect(w.get('[data-option="fantasy-knight-pf"]').attributes('tabindex')).toBe('0')
    await w.get('[data-filter="horror"]').trigger('click')
    // Hidden selection: Automatic takes the tab stop so the group is still reachable.
    expect(w.get('[data-option=""]').attributes('tabindex')).toBe('0')
    expect(radios(w).filter((r) => r.attributes('tabindex') === '0')).toHaveLength(1)
    w.unmount()
  })

  it('uses a roving tabindex: only the selected tile is tabbable', () => {
    const w = mountPicker({ modelValue: 'cosmic-horror-cc' })
    const tabbable = radios(w).filter((r) => r.attributes('tabindex') === '0')
    expect(tabbable.map((r) => r.attributes('data-option'))).toEqual(['cosmic-horror-cc'])
    w.unmount()
  })

  describe('keyboard', () => {
    const press = (w: ReturnType<typeof mountPicker>, key: string) =>
      w.get('[role="radiogroup"]').trigger('keydown', { key })

    it('moves the selection with the arrow keys and wraps around', async () => {
      const w = mountPicker({ modelValue: null })
      await press(w, 'ArrowRight')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([THUMBNAIL_MANIFEST[0].key])
      await press(w, 'ArrowLeft')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([null])
      await press(w, 'ArrowLeft')   // wraps to the last
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([THUMBNAIL_MANIFEST.at(-1)!.key])
      await press(w, 'ArrowDown')   // wraps to Automatic
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([null])
      await press(w, 'ArrowUp')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([THUMBNAIL_MANIFEST.at(-1)!.key])
      w.unmount()
    })

    it('Home and End jump to the first and last option', async () => {
      const w = mountPicker({ modelValue: 'cosmic-horror-cc' })
      await press(w, 'End')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([THUMBNAIL_MANIFEST.at(-1)!.key])
      await press(w, 'Home')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([null])
      w.unmount()
    })

    it('moves focus along with the selection', async () => {
      const w = mountPicker({ modelValue: null })
      ;(w.get('[data-option=""]').element as HTMLElement).focus()
      await press(w, 'ArrowRight')
      await w.vm.$nextTick()
      await w.vm.$nextTick()
      expect(document.activeElement?.getAttribute('data-option')).toBe(THUMBNAIL_MANIFEST[0].key)
      w.unmount()
    })

    it('only moves within the filtered tiles', async () => {
      const w = mountPicker({ modelValue: null })
      await w.get('[data-filter="homebrew"]').trigger('click')
      await press(w, 'ArrowRight')
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual(['homebrew'])
      await press(w, 'ArrowRight')   // only Automatic and Homebrew are visible, so it wraps
      expect(w.emitted('update:modelValue')!.at(-1)).toEqual([null])
      w.unmount()
    })

    it('ignores other keys', async () => {
      const w = mountPicker()
      await press(w, 'a')
      expect(w.emitted('update:modelValue')).toBeUndefined()
      w.unmount()
    })
  })

  it('lazy-loads each image and takes its alt text from the label, inside a 3:4 wrapper', () => {
    const w = mountPicker()
    const tile = w.get('[data-option="fantasy-knight-pf"]')
    const img = tile.get('img')
    expect(img.attributes('loading')).toBe('lazy')
    expect(img.attributes('alt')).toBe('Fantasy knight')
    expect(tile.find('div.aspect-\\[3\\/4\\]').exists()).toBe(true)
    w.unmount()
  })

  it('gives every tile and chip a tap target of at least 44 px', () => {
    const w = mountPicker()
    for (const el of [...w.findAll('[role="radio"]'), ...w.findAll('[data-filter]')]) {
      expect(el.classes().some((c) => c === 'min-h-[44px]')).toBe(true)
    }
    w.unmount()
  })

  it('says when the stored thumbnail is no longer available', () => {
    const w = mountPicker({ unavailable: true })
    expect(w.get('[role="status"]').text()).toContain('This thumbnail is no longer available')
    w.unmount()
    expect(mountPicker().find('[role="status"]').exists()).toBe(false)
  })
})

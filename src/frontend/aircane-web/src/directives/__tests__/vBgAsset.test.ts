import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import vBgAsset from '../vBgAsset'

/**
 * Capture the Image instances created by the directive so tests can drive the
 * onload / onerror branches deterministically instead of relying on real network.
 */
let created: FakeImage[] = []

class FakeImage {
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  private _src = ''
  constructor() {
    created.push(this)
  }
  set src(value: string) {
    this._src = value
  }
  get src(): string {
    return this._src
  }
}

function mountWithDirective(value: Record<string, unknown>) {
  const Host = defineComponent({
    props: { opts: { type: Object, required: true } },
    template: `<div class="target" v-bg-asset="opts"></div>`,
  })
  return mount(Host, {
    props: { opts: value },
    global: { directives: { 'bg-asset': vBgAsset } },
  })
}

describe('vBgAsset directive', () => {
  const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})

  beforeEach(() => {
    created = []
    warnSpy.mockClear()
    vi.stubGlobal('Image', FakeImage)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('applies the fallback colour on a 404 and warns (no background-image)', () => {
    const wrapper = mountWithDirective({
      url: '/assets/missing.png',
      fallback: '#123456',
    })
    const el = wrapper.find('.target').element as HTMLElement

    // Fallback colour is set synchronously on mount.
    expect(el.style.backgroundColor).toBe('rgb(18, 52, 86)')

    // Drive the error branch.
    created[0]!.onerror?.()

    expect(el.style.backgroundImage).toBe('')
    expect(warnSpy).toHaveBeenCalledWith(
      '[Aircane] Background asset failed to load: /assets/missing.png',
    )
  })

  it('applies the background-image on a successful load', () => {
    const wrapper = mountWithDirective({
      url: '/assets/ok.png',
      fallback: '#0a0a1a',
      size: 'contain',
      position: 'top',
    })
    const el = wrapper.find('.target').element as HTMLElement

    created[0]!.onload?.()

    expect(el.style.backgroundImage).toContain("/assets/ok.png")
    expect(el.style.backgroundSize).toBe('contain')
    expect(el.style.backgroundPosition).toContain('top')
    expect(warnSpy).not.toHaveBeenCalled()
  })
})

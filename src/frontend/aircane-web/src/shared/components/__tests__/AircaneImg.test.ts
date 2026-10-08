import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import AircaneImg from '../AircaneImg.vue'

describe('AircaneImg', () => {
  it('shows the fallback slot when the src fails to load and warns', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mount(AircaneImg, {
      props: { src: '/assets/missing.png', type: 'thumbnail' },
      slots: { fallback: '<div class="ph">placeholder</div>' },
    })

    await wrapper.find('img').trigger('error')

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('.ph').exists()).toBe(true)
    expect(warnSpy).toHaveBeenCalledWith(
      '[Aircane] Asset failed to load: /assets/missing.png',
    )
    warnSpy.mockRestore()
  })

  it('shows the img and forwards attrs when the src loads', () => {
    const wrapper = mount(AircaneImg, {
      props: { src: '/assets/ok.png', alt: 'A thing', type: 'thumbnail' },
      attrs: { 'data-test': 'forwarded' },
    })

    const img = wrapper.find('img')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toBe('/assets/ok.png')
    expect(img.attributes('alt')).toBe('A thing')
    // $attrs land on the img (inheritAttrs:false).
    expect(img.attributes('data-test')).toBe('forwarded')
  })
})

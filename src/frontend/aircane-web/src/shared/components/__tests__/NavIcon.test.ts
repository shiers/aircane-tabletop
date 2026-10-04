import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import NavIcon from '../NavIcon.vue'

describe('NavIcon', () => {
  it('shows the emoji fallback when the PNG fails to load', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mount(NavIcon, {
      props: { icon: 'icon-campaign', label: 'Campaigns' },
    })

    await wrapper.find('img').trigger('error')

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('.fallback').text()).toBe('🔖')
    warnSpy.mockRestore()
  })

  it('shows the PNG when available', () => {
    const wrapper = mount(NavIcon, {
      props: { icon: 'icon-campaign', label: 'Campaigns' },
    })

    const img = wrapper.find('img')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toBe('/assets/ui/icon-campaign.png')
    expect(img.attributes('alt')).toBe('Campaigns')
    expect(wrapper.find('.fallback').exists()).toBe(false)
  })
})

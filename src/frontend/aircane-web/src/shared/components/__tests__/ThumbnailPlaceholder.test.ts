import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ThumbnailPlaceholder from '../ThumbnailPlaceholder.vue'

describe('ThumbnailPlaceholder', () => {
  it('shows the correct initials from a label', () => {
    const wrapper = mount(ThumbnailPlaceholder, {
      props: { label: 'Shadow Vale' },
    })
    expect(wrapper.find('.initials').text()).toBe('SV')
  })

  it('shows only the ✦ ornament when there is no label', () => {
    const wrapper = mount(ThumbnailPlaceholder)
    expect(wrapper.find('.ornament').text()).toBe('✦')
    expect(wrapper.find('.initials').exists()).toBe(false)
  })
})

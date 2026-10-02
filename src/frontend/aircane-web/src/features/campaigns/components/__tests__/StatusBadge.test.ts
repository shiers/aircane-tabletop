import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '../StatusBadge.vue'

describe('StatusBadge', () => {
  it('renders the active state with a green dot and label', () => {
    const wrapper = mount(StatusBadge, { props: { status: 'active' } })
    expect(wrapper.classes()).toContain('active')
    expect(wrapper.find('.dot').exists()).toBe(true)
    expect(wrapper.text()).toContain('Active')
  })

  it('renders the paused state with the pause glyph', () => {
    const wrapper = mount(StatusBadge, { props: { status: 'paused' } })
    expect(wrapper.classes()).toContain('paused')
    expect(wrapper.find('.glyph').exists()).toBe(true)
    expect(wrapper.text()).toContain('⏸')
    expect(wrapper.text()).toContain('Paused')
  })

  it('renders the ended state with a neutral dot', () => {
    const wrapper = mount(StatusBadge, { props: { status: 'ended' } })
    expect(wrapper.classes()).toContain('ended')
    expect(wrapper.find('.dot').exists()).toBe(true)
    expect(wrapper.find('.glyph').exists()).toBe(false)
    expect(wrapper.text()).toContain('Ended')
  })
})

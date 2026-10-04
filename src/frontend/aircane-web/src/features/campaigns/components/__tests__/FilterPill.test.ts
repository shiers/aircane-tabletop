import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import FilterPill from '../FilterPill.vue'

describe('FilterPill', () => {
  it('renders the label and the grid icon', () => {
    const wrapper = mount(FilterPill, { props: { label: 'All' } })
    expect(wrapper.text()).toContain('All')
    expect(wrapper.find('.icon').text()).toBe('⊞')
  })

  it('renders a count pill only when a count is provided', () => {
    const withCount = mount(FilterPill, { props: { label: 'Active', count: 3 } })
    expect(withCount.find('.count').exists()).toBe(true)
    expect(withCount.find('.count').text()).toBe('3')

    const withoutCount = mount(FilterPill, { props: { label: 'Active' } })
    expect(withoutCount.find('.count').exists()).toBe(false)
  })

  it('applies the active class and aria-pressed when active', () => {
    const wrapper = mount(FilterPill, { props: { label: 'Active', active: true } })
    expect(wrapper.classes()).toContain('active')
    expect(wrapper.attributes('aria-pressed')).toBe('true')
  })

  it('emits click when pressed', async () => {
    const wrapper = mount(FilterPill, { props: { label: 'All' } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })
})

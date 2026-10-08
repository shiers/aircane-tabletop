import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ModeToggle from '../ModeToggle.vue'

describe('ModeToggle', () => {
  it('renders Solo and Group options with the active one checked', () => {
    const wrapper = mount(ModeToggle, { props: { modelValue: 'solo' } })
    const buttons = wrapper.findAll('button[role="radio"]')
    expect(buttons).toHaveLength(2)
    expect(wrapper.text()).toContain('Solo')
    expect(wrapper.text()).toContain('Group')
    expect(buttons[0].attributes('aria-checked')).toBe('true')
    expect(buttons[1].attributes('aria-checked')).toBe('false')
  })

  it('emits update:modelValue with the clicked mode', async () => {
    const wrapper = mount(ModeToggle, { props: { modelValue: 'solo' } })
    const buttons = wrapper.findAll('button[role="radio"]')
    await buttons[1].trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['group'])

    await buttons[0].trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[1]).toEqual(['solo'])
  })
})

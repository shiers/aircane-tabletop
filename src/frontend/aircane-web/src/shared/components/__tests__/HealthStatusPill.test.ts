import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import HealthStatusPill from '../HealthStatusPill.vue'

describe('HealthStatusPill', () => {
  it('renders the healthy state with the label and healthy class', () => {
    const wrapper = mount(HealthStatusPill, {
      props: { status: 'healthy', label: 'Backend: healthy' },
    })
    const pill = wrapper.find('.health-pill')
    expect(pill.exists()).toBe(true)
    expect(pill.classes()).toContain('healthy')
    expect(pill.classes()).not.toContain('error')
    expect(wrapper.text()).toContain('Backend: healthy')
  })

  it('renders the error state with the label and error class', () => {
    const wrapper = mount(HealthStatusPill, {
      props: { status: 'error', label: 'Backend: unreachable' },
    })
    const pill = wrapper.find('.health-pill')
    expect(pill.exists()).toBe(true)
    expect(pill.classes()).toContain('error')
    expect(pill.classes()).not.toContain('healthy')
    expect(wrapper.text()).toContain('Backend: unreachable')
  })
})

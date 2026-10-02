import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ContentRatioSlider from '../ContentRatioSlider.vue'

describe('ContentRatioSlider', () => {
  it('renders the three ratios with their labels and percentages', () => {
    const wrapper = mount(ContentRatioSlider, {
      props: { combat: 34, exploration: 33, roleplay: 33 },
    })
    expect(wrapper.findAll('input[type="range"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('Combat')
    expect(wrapper.text()).toContain('Exploration')
    expect(wrapper.text()).toContain('Roleplay')
    expect(wrapper.text()).toContain('34%')
  })

  it('emits update:combat when the combat slider moves', async () => {
    const wrapper = mount(ContentRatioSlider, {
      props: { combat: 34, exploration: 33, roleplay: 33 },
    })
    await wrapper.find('#combat-ratio').setValue('50')
    expect(wrapper.emitted('update:combat')?.[0]).toEqual([50])
  })

  it('emits update:exploration when the exploration slider moves', async () => {
    const wrapper = mount(ContentRatioSlider, {
      props: { combat: 34, exploration: 33, roleplay: 33 },
    })
    await wrapper.find('#exploration-ratio').setValue('20')
    expect(wrapper.emitted('update:exploration')?.[0]).toEqual([20])
  })

  it('emits update:roleplay when the roleplay slider moves', async () => {
    const wrapper = mount(ContentRatioSlider, {
      props: { combat: 34, exploration: 33, roleplay: 33 },
    })
    await wrapper.find('#roleplay-ratio').setValue('10')
    expect(wrapper.emitted('update:roleplay')?.[0]).toEqual([10])
  })
})

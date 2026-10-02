import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import GameSystemDropdown from '../GameSystemDropdown.vue'

const options = [
  { id: 'dnd5e-2014', name: 'D&D 5e 2014' },
  { id: 'pf2e', name: 'Pathfinder 2e' },
]

describe('GameSystemDropdown', () => {
  it('renders an option per provided game system plus the "Any" placeholder', () => {
    const wrapper = mount(GameSystemDropdown, {
      props: { modelValue: null, options },
    })
    const values = wrapper.findAll('option').map((o) => o.attributes('value'))
    expect(values).toEqual(['', 'dnd5e-2014', 'pf2e'])
    expect(wrapper.find('select').exists()).toBe(true)
  })

  it('reflects the selected modelValue', () => {
    const wrapper = mount(GameSystemDropdown, {
      props: { modelValue: 'pf2e', options },
    })
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('pf2e')
  })

  it('emits update:modelValue with the selected id', async () => {
    const wrapper = mount(GameSystemDropdown, {
      props: { modelValue: null, options },
    })
    await wrapper.find('select').setValue('dnd5e-2014')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['dnd5e-2014'])
  })

  it('emits null when the "Any" placeholder is selected', async () => {
    const wrapper = mount(GameSystemDropdown, {
      props: { modelValue: 'pf2e', options },
    })
    await wrapper.find('select').setValue('')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([null])
  })
})

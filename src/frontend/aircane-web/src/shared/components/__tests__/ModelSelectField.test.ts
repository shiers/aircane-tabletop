import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ModelSelectField from '../ModelSelectField.vue'

describe('ModelSelectField', () => {
  it('renders an option for each provided model', () => {
    const wrapper = mount(ModelSelectField, {
      props: { modelValue: 'gpt-4o', options: ['gpt-4o', 'gpt-4o-mini', 'o1'] },
    })
    expect(wrapper.find('select').exists()).toBe(true)
    const optionValues = wrapper.findAll('option').map((o) => o.attributes('value'))
    expect(optionValues).toEqual(['gpt-4o', 'gpt-4o-mini', 'o1'])
  })

  it('reflects the bound modelValue as the selected option', () => {
    const wrapper = mount(ModelSelectField, {
      props: { modelValue: 'gpt-4o-mini', options: ['gpt-4o', 'gpt-4o-mini'] },
    })
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('gpt-4o-mini')
  })

  it('emits update:modelValue on change', async () => {
    const wrapper = mount(ModelSelectField, {
      props: { modelValue: 'gpt-4o', options: ['gpt-4o', 'gpt-4o-mini'] },
    })
    await wrapper.find('select').setValue('gpt-4o-mini')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['gpt-4o-mini'])
  })

  it('forwards the id attribute to the select', () => {
    const wrapper = mount(ModelSelectField, {
      props: { modelValue: 'a', options: ['a'], id: 'my-model' },
    })
    expect(wrapper.find('select').attributes('id')).toBe('my-model')
  })
})

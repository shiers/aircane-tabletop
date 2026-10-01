import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ModelSelect from '../ModelSelect.vue'

describe('ModelSelect', () => {
  it('renders a dropdown of the fetched models (no Custom option)', () => {
    const wrapper = mount(ModelSelect, {
      props: { modelValue: 'gpt-4o', models: ['gpt-4o', 'gpt-4o-mini'] },
    })
    expect(wrapper.find('select').exists()).toBe(true)
    expect(wrapper.find('input').exists()).toBe(false)

    const optionValues = wrapper.findAll('option').map((o) => o.attributes('value'))
    expect(optionValues).toEqual(['gpt-4o', 'gpt-4o-mini'])
    // The old "Custom…" sentinel must be gone.
    expect(wrapper.text()).not.toContain('Custom')
  })

  it('emits the selected model on change', async () => {
    const wrapper = mount(ModelSelect, {
      props: { modelValue: 'gpt-4o', models: ['gpt-4o', 'gpt-4o-mini'] },
    })
    await wrapper.find('select').setValue('gpt-4o-mini')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['gpt-4o-mini'])
  })

  it('falls back to a free-text input when no models are available', async () => {
    const wrapper = mount(ModelSelect, {
      props: { modelValue: '', models: [], placeholder: 'gpt-4o-mini' },
    })
    expect(wrapper.find('select').exists()).toBe(false)
    const input = wrapper.find('input')
    expect(input.exists()).toBe(true)
    expect(input.attributes('placeholder')).toBe('gpt-4o-mini')

    await input.setValue('my-custom-deployment')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['my-custom-deployment'])
  })

  it('keeps a saved value visible when it is not in the fetched list', () => {
    const wrapper = mount(ModelSelect, {
      props: { modelValue: 'gpt-4o-2024-11-20', models: ['gpt-4o', 'gpt-4o-mini'] },
    })
    const optionValues = wrapper.findAll('option').map((o) => o.attributes('value'))
    // The saved value is prepended so the selection stays valid/visible.
    expect(optionValues).toContain('gpt-4o-2024-11-20')
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('gpt-4o-2024-11-20')
  })
})

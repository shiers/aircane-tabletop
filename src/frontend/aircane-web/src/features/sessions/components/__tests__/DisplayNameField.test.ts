import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import DisplayNameField from '../DisplayNameField.vue'

describe('DisplayNameField', () => {
  it('emits update:modelValue when the user types', async () => {
    const wrapper = mount(DisplayNameField, { props: { modelValue: '' } })
    await wrapper.find('input').setValue('Thorin')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['Thorin'])
  })

  it('renders the error when one is provided', () => {
    const wrapper = mount(DisplayNameField, {
      props: { modelValue: '', error: 'Display name is required.' },
    })
    expect(wrapper.text()).toContain('Display name is required.')
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('does not render an error area when no error is provided', () => {
    const wrapper = mount(DisplayNameField, { props: { modelValue: 'Thorin' } })
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('reflects the modelValue on the input', () => {
    const wrapper = mount(DisplayNameField, { props: { modelValue: 'Gandalf' } })
    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('Gandalf')
  })
})

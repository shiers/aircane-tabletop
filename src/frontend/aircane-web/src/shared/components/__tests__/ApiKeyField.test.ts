import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ApiKeyField from '../ApiKeyField.vue'

describe('ApiKeyField', () => {
  it('renders a masked password input by default with the given placeholder', () => {
    const wrapper = mount(ApiKeyField, {
      props: { modelValue: '', placeholder: 'xai-...' },
    })
    const input = wrapper.find('input')
    expect(input.exists()).toBe(true)
    expect(input.attributes('type')).toBe('password')
    expect(input.attributes('placeholder')).toBe('xai-...')
  })

  it('defaults the placeholder to sk-...', () => {
    const wrapper = mount(ApiKeyField, { props: { modelValue: '' } })
    expect(wrapper.find('input').attributes('placeholder')).toBe('sk-...')
  })

  it('toggles the input type between password and text when the eye button is clicked', async () => {
    const wrapper = mount(ApiKeyField, { props: { modelValue: 'secret' } })
    const input = wrapper.find('input')
    expect(input.attributes('type')).toBe('password')

    await wrapper.find('button').trigger('click')
    expect(wrapper.find('input').attributes('type')).toBe('text')

    await wrapper.find('button').trigger('click')
    expect(wrapper.find('input').attributes('type')).toBe('password')
  })

  it('emits update:modelValue with the typed value', async () => {
    const wrapper = mount(ApiKeyField, { props: { modelValue: '' } })
    await wrapper.find('input').setValue('sk-abc123')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['sk-abc123'])
  })

  it('does not transform the bound value (keeps a masked value compatible with isKeyMasked)', () => {
    const wrapper = mount(ApiKeyField, { props: { modelValue: '****abcd' } })
    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('****abcd')
    // No emission happens just from binding a masked value.
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
  })
})

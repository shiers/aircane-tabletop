import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import InviteCodeField from '../InviteCodeField.vue'

describe('InviteCodeField', () => {
  it('emits update:modelValue when the user types', async () => {
    const wrapper = mount(InviteCodeField, { props: { modelValue: '' } })
    await wrapper.find('input').setValue('ABC123')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['ABC123'])
  })

  it('limits the invite code to 8 characters', () => {
    const wrapper = mount(InviteCodeField, { props: { modelValue: '' } })
    expect(wrapper.find('input').attributes('maxlength')).toBe('8')
  })

  it('renders the error when one is provided', () => {
    const wrapper = mount(InviteCodeField, {
      props: { modelValue: '', error: 'Invite code is required.' },
    })
    expect(wrapper.text()).toContain('Invite code is required.')
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('does not render an error area when no error is provided', () => {
    const wrapper = mount(InviteCodeField, { props: { modelValue: 'ABC' } })
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('reflects the modelValue on the input', () => {
    const wrapper = mount(InviteCodeField, { props: { modelValue: 'XYZ789' } })
    expect((wrapper.find('input').element as HTMLInputElement).value).toBe('XYZ789')
  })
})

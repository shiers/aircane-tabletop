import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AbilityScoreTile from '../AbilityScoreTile.vue'
import vBgAsset from '@/directives/vBgAsset'

const global = { directives: { 'bg-asset': vBgAsset } }

describe('AbilityScoreTile', () => {
  it('renders the abbreviation and score', () => {
    const wrapper = mount(AbilityScoreTile, { props: { abbr: 'STR', score: 16, modifier: 3 }, global })
    expect(wrapper.find('.abbr').text()).toBe('STR')
    expect(wrapper.find('.score').text()).toBe('16')
  })

  it('renders a positive modifier with a plus sign', () => {
    const wrapper = mount(AbilityScoreTile, { props: { abbr: 'DEX', score: 14, modifier: 2 }, global })
    expect(wrapper.find('.modifier').text()).toBe('+2')
  })

  it('renders a zero modifier as +0', () => {
    const wrapper = mount(AbilityScoreTile, { props: { abbr: 'CON', score: 10, modifier: 0 }, global })
    expect(wrapper.find('.modifier').text()).toBe('+0')
  })

  it('renders a negative modifier with a minus sign', () => {
    const wrapper = mount(AbilityScoreTile, { props: { abbr: 'INT', score: 8, modifier: -1 }, global })
    expect(wrapper.find('.modifier').text()).toBe('-1')
  })
})

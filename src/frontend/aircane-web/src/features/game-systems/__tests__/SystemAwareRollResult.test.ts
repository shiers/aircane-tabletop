import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SystemAwareRollResult from '../components/SystemAwareRollResult.vue'
import type { SystemAwareRollResult as RollResultType } from '../types'

const standardRoll: RollResultType = {
  id: 'roll-1',
  formula: '1d20+5',
  dieResults: [14],
  modifier: 5,
  total: 19,
  successCount: null,
  outcomeTier: null,
  thresholdBands: null,
  conventionType: 'single_die_modifier',
  context: 'Attack roll vs goblin',
  createdAt: '2024-01-01T10:00:00Z',
}

const poolRoll: RollResultType = {
  id: 'roll-2',
  formula: '6d6>=5',
  dieResults: [2, 5, 6, 3, 5, 1],
  modifier: 0,
  total: 22,
  successCount: 3,
  outcomeTier: null,
  thresholdBands: null,
  conventionType: 'dice_pool_success',
  context: 'Hacking attempt',
  createdAt: '2024-01-01T10:01:00Z',
}

const thresholdRoll: RollResultType = {
  id: 'roll-3',
  formula: '2d6+1',
  dieResults: [4, 5],
  modifier: 1,
  total: 10,
  successCount: null,
  outcomeTier: 'Strong Hit',
  thresholdBands: [
    { name: 'Miss', min: 2, max: 6 },
    { name: 'Weak Hit', min: 7, max: 9 },
    { name: 'Strong Hit', min: 10, max: null },
  ],
  conventionType: 'fixed_dice_threshold',
  context: 'Defy Danger',
  createdAt: '2024-01-01T10:02:00Z',
}

describe('SystemAwareRollResult', () => {
  describe('standard roll (d20 system)', () => {
    it('renders the formula', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: standardRoll } })
      expect(wrapper.text()).toContain('1d20+5')
    })

    it('renders individual die results', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: standardRoll } })
      expect(wrapper.text()).toContain('14')
    })

    it('renders the modifier', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: standardRoll } })
      expect(wrapper.text()).toContain('+5')
    })

    it('renders the total', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: standardRoll } })
      expect(wrapper.text()).toContain('19')
    })

    it('renders the context', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: standardRoll } })
      expect(wrapper.text()).toContain('Attack roll vs goblin')
    })
  })

  describe('pool roll (dice pool system)', () => {
    it('renders success count instead of total', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: poolRoll } })
      expect(wrapper.text()).toContain('Successes')
      expect(wrapper.text()).toContain('3')
    })

    it('renders all individual die results', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: poolRoll } })
      expect(wrapper.text()).toContain('2')
      expect(wrapper.text()).toContain('5')
      expect(wrapper.text()).toContain('6')
      expect(wrapper.text()).toContain('3')
      expect(wrapper.text()).toContain('1')
    })
  })

  describe('threshold roll (PbtA system)', () => {
    it('renders the outcome tier', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: thresholdRoll } })
      expect(wrapper.text()).toContain('Strong Hit')
    })

    it('renders threshold bands', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: thresholdRoll } })
      expect(wrapper.text()).toContain('Miss')
      expect(wrapper.text()).toContain('Weak Hit')
      expect(wrapper.text()).toContain('Strong Hit')
    })

    it('highlights the active threshold band', () => {
      const wrapper = mount(SystemAwareRollResult, { props: { roll: thresholdRoll } })
      const activeBand = wrapper.find('.bg-aircane-600')
      expect(activeBand.exists()).toBe(true)
      expect(activeBand.text()).toContain('Strong Hit')
    })
  })

  describe('outcome tier styling', () => {
    it('applies success styling for success outcomes', () => {
      const roll = { ...standardRoll, outcomeTier: 'Critical Success' }
      const wrapper = mount(SystemAwareRollResult, { props: { roll } })
      const tierEl = wrapper.find('.text-yellow-400')
      expect(tierEl.exists()).toBe(true)
    })

    it('applies failure styling for failure outcomes', () => {
      const roll = { ...standardRoll, outcomeTier: 'Failure' }
      const wrapper = mount(SystemAwareRollResult, { props: { roll } })
      const tierEl = wrapper.find('.text-red-400')
      expect(tierEl.exists()).toBe(true)
    })
  })
})

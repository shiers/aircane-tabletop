import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ActionEconomyTracker from '../components/ActionEconomyTracker.vue'
import type { ActionBudget } from '../types'

const namedSlotsBudget: ActionBudget = {
  type: 'named_slots',
  slots: [
    { name: 'action', label: 'Action', total: 1, remaining: 1 },
    { name: 'bonus_action', label: 'Bonus Action', total: 1, remaining: 0 },
    { name: 'reaction', label: 'Reaction', total: 1, remaining: 1 },
    { name: 'free_action', label: 'Free Action', total: -1, remaining: -1 },
  ],
}

const actionPointsBudget: ActionBudget = {
  type: 'action_points',
  slots: [],
  totalPoints: 6,
  remainingPoints: 4,
}

const freeformBudget: ActionBudget = {
  type: 'freeform',
  slots: [],
}

describe('ActionEconomyTracker', () => {
  it('renders nothing for freeform systems', () => {
    const wrapper = mount(ActionEconomyTracker, {
      props: { budget: freeformBudget },
    })
    expect(wrapper.text()).toBe('')
  })

  it('renders nothing when budget is null', () => {
    const wrapper = mount(ActionEconomyTracker, {
      props: { budget: null },
    })
    expect(wrapper.text()).toBe('')
  })

  describe('named slots', () => {
    it('renders all action slots', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: namedSlotsBudget },
      })
      expect(wrapper.text()).toContain('Action')
      expect(wrapper.text()).toContain('Bonus Action')
      expect(wrapper.text()).toContain('Reaction')
      expect(wrapper.text()).toContain('Free Action')
    })

    it('shows Use buttons for each slot', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: namedSlotsBudget },
      })
      const useButtons = wrapper.findAll('button').filter((b) => b.text() === 'Use')
      expect(useButtons.length).toBeGreaterThan(0)
    })

    it('disables Use button when slot is exhausted', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: namedSlotsBudget },
      })
      // Bonus Action has remaining: 0
      const buttons = wrapper.findAll('button').filter((b) => b.text() === 'Use')
      const bonusActionButton = buttons[1] // second slot
      expect(bonusActionButton.attributes('disabled')).toBeDefined()
    })

    it('emits consume when Use is clicked', async () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: namedSlotsBudget },
      })
      const buttons = wrapper.findAll('button').filter((b) => b.text() === 'Use')
      await buttons[0].trigger('click')

      expect(wrapper.emitted('consume')).toBeTruthy()
      expect(wrapper.emitted('consume')![0]).toEqual(['action'])
    })

    it('shows infinity symbol for unlimited slots', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: namedSlotsBudget },
      })
      expect(wrapper.text()).toContain('∞')
    })
  })

  describe('action points', () => {
    it('renders action points display', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: actionPointsBudget },
      })
      expect(wrapper.text()).toContain('Action Points')
      expect(wrapper.text()).toContain('4 / 6')
    })

    it('renders progress bar', () => {
      const wrapper = mount(ActionEconomyTracker, {
        props: { budget: actionPointsBudget },
      })
      const progressBar = wrapper.find('.bg-aircane-500')
      expect(progressBar.exists()).toBe(true)
    })
  })
})

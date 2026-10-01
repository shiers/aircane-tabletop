import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ConditionDisplay from '../components/ConditionDisplay.vue'
import type { ActiveCondition } from '../types'

const mockConditions: ActiveCondition[] = [
  {
    name: 'Poisoned',
    description: 'Disadvantage on attack rolls and ability checks.',
    durationType: 'until_save',
    roundsRemaining: null,
  },
  {
    name: 'Prone',
    description: 'Disadvantage on attack rolls.',
    durationType: 'until_action',
    endCondition: 'Use half movement to stand',
    roundsRemaining: null,
  },
  {
    name: 'Haste',
    description: 'Double speed, +2 AC.',
    durationType: 'rounds',
    roundsRemaining: 3,
  },
]

describe('ConditionDisplay', () => {
  it('renders the heading', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('Conditions')
  })

  it('renders all conditions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('Poisoned')
    expect(wrapper.text()).toContain('Prone')
    expect(wrapper.text()).toContain('Haste')
  })

  it('shows condition descriptions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('Disadvantage on attack rolls and ability checks.')
  })

  it('shows duration for round-based conditions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('3 rounds remaining')
  })

  it('shows "Until save" for save-based conditions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('Until save')
  })

  it('shows end condition for action-based conditions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    expect(wrapper.text()).toContain('Use half movement to stand')
  })

  it('shows empty state when no conditions', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: [] },
    })
    expect(wrapper.text()).toContain('No active conditions')
  })

  it('emits remove when remove button is clicked', async () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: mockConditions },
    })
    const removeButtons = wrapper.findAll('button[title="Remove condition"]')
    await removeButtons[0].trigger('click')

    expect(wrapper.emitted('remove')).toBeTruthy()
    expect(wrapper.emitted('remove')![0]).toEqual(['Poisoned'])
  })

  it('shows Add button in freeform mode', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: [], freeform: true },
    })
    expect(wrapper.text()).toContain('+ Add')
  })

  it('does not show Add button when not freeform', () => {
    const wrapper = mount(ConditionDisplay, {
      props: { conditions: [] },
    })
    expect(wrapper.text()).not.toContain('+ Add')
  })
})

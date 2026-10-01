import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CombatTracker from '../CombatTracker.vue'
import type { EncounterStateDto, CombatantDto } from '../combat'

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function combatant(overrides: Partial<CombatantDto> = {}): CombatantDto {
  return {
    id: 'c1',
    name: 'Hero',
    isPlayerCharacter: true,
    currentHp: 20,
    maxHp: 20,
    temporaryHp: 0,
    initiative: 15,
    isDowned: false,
    isDead: false,
    conditions: [],
    deathSaves: null,
    ...overrides,
  }
}

function encounter(overrides: Partial<EncounterStateDto> = {}): EncounterStateDto {
  const hero = combatant({ id: 'hero', name: 'Hero', initiative: 18 })
  const goblin = combatant({
    id: 'goblin',
    name: 'Goblin',
    isPlayerCharacter: false,
    currentHp: 3,
    maxHp: 12,
    initiative: 10,
  })
  return {
    encounterId: 'enc-1',
    isActive: true,
    round: 2,
    turnIndex: 0,
    activeCombatantId: 'hero',
    initiativeOrder: ['hero', 'goblin'],
    combatants: [hero, goblin],
    ...overrides,
  }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CombatTracker', () => {
  it('renders nothing when there is no active encounter', () => {
    const wrapper = mount(CombatTracker, { props: { encounter: null } })
    expect(wrapper.find('[aria-label="Combat tracker"]').exists()).toBe(false)
  })

  it('renders the round number and initiative order', () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter() } })
    expect(wrapper.text()).toContain('Round 2')
    const names = wrapper.findAll('li').map((li) => li.text())
    expect(names[0]).toContain('Hero')
    expect(names[1]).toContain('Goblin')
  })

  it('marks the active combatant with aria-current', () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter() } })
    const active = wrapper.find('li[aria-current="true"]')
    expect(active.exists()).toBe(true)
    expect(active.text()).toContain('Hero')
  })

  it('moves the active highlight when the encounter turn advances', async () => {
    const enc = encounter()
    const wrapper = mount(CombatTracker, { props: { encounter: enc } })
    expect(wrapper.find('li[aria-current="true"]').text()).toContain('Hero')

    // Simulate a CombatTurnChanged-driven refresh: goblin is now active.
    await wrapper.setProps({
      encounter: { ...enc, turnIndex: 1, activeCombatantId: 'goblin' },
    })
    expect(wrapper.find('li[aria-current="true"]').text()).toContain('Goblin')
  })

  it('shows exact HP for all combatants in host (non-readonly) mode', () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter(), readonly: false } })
    expect(wrapper.text()).toContain('20/20') // hero
    expect(wrapper.text()).toContain('3/12') // goblin exact HP visible to host
  })

  it('hides other combatants exact HP in readonly (player) mode, showing a band instead', () => {
    const wrapper = mount(CombatTracker, {
      props: { encounter: encounter(), readonly: true, ownCharacterId: 'hero' },
    })
    // Own character exact HP visible.
    expect(wrapper.text()).toContain('20/20')
    // Goblin exact HP hidden; a rough band is shown instead.
    expect(wrapper.text()).not.toContain('3/12')
    expect(wrapper.text()).toContain('Critical') // 3/12 = 25% -> Critical
  })

  it('emits advance-turn when Next Turn is clicked (host mode)', async () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter(), readonly: false } })
    const nextBtn = wrapper.findAll('button').find((b) => b.text() === 'Next Turn')
    expect(nextBtn).toBeTruthy()
    await nextBtn!.trigger('click')
    expect(wrapper.emitted('advance-turn')).toBeTruthy()
  })

  it('does not render host controls in readonly mode', () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter(), readonly: true } })
    const nextBtn = wrapper.findAll('button').find((b) => b.text() === 'Next Turn')
    expect(nextBtn).toBeUndefined()
  })

  it('emits apply-damage with target and amount from the quick-action popover', async () => {
    const wrapper = mount(CombatTracker, { props: { encounter: encounter(), readonly: false } })

    const damageBtn = wrapper.findAll('button').find((b) => b.text() === 'Apply Damage')
    await damageBtn!.trigger('click')

    // Select the goblin and enter an amount.
    const select = wrapper.find('select')
    await select.setValue('goblin')
    const amount = wrapper.find('input[type="number"]')
    await amount.setValue(5)

    const applyBtn = wrapper.findAll('button').find((b) => b.text() === 'Apply')
    await applyBtn!.trigger('click')

    const emitted = wrapper.emitted('apply-damage')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['goblin', 5])
  })

  it('shows action slots for the active combatant only when enabled', () => {
    const withSlots = mount(CombatTracker, {
      props: {
        encounter: encounter(),
        readonly: false,
        showActionSlots: true,
        actionSlots: ['Action', 'Bonus Action', 'Reaction'],
      },
    })
    expect(withSlots.text()).toContain('Bonus Action')

    const freeform = mount(CombatTracker, {
      props: { encounter: encounter(), readonly: false, showActionSlots: false, actionSlots: [] },
    })
    expect(freeform.text()).not.toContain('Bonus Action')
  })

  it('renders condition chips', () => {
    const enc = encounter()
    enc.combatants[1].conditions = [
      { name: 'Poisoned', remainingRounds: 3, appliedOnRound: 1, endCondition: null },
    ]
    const wrapper = mount(CombatTracker, { props: { encounter: enc } })
    expect(wrapper.text()).toContain('Poisoned')
    expect(wrapper.text()).toContain('(3r)')
  })
})

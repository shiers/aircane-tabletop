import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RoleBadge from '../RoleBadge.vue'
import vBgAsset from '@/directives/vBgAsset'

const global = { directives: { 'bg-asset': vBgAsset } }

describe('RoleBadge', () => {
  it('renders the Player state with the player class and label', () => {
    const wrapper = mount(RoleBadge, { props: { role: 'Player' }, global })
    expect(wrapper.classes()).toContain('player')
    expect(wrapper.find('.label').text()).toBe('Player')
  })

  it('renders the NPC state with the npc class and label', () => {
    const wrapper = mount(RoleBadge, { props: { role: 'NPC' }, global })
    expect(wrapper.classes()).toContain('npc')
    expect(wrapper.find('.label').text()).toBe('NPC')
  })
})

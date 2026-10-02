import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RoleBadge from '../RoleBadge.vue'

describe('RoleBadge', () => {
  it('renders the Player state with the player class and label', () => {
    const wrapper = mount(RoleBadge, { props: { role: 'Player' } })
    expect(wrapper.classes()).toContain('player')
    expect(wrapper.find('.label').text()).toBe('Player')
    expect(wrapper.attributes('style')).toContain('player-badge-art.png')
  })

  it('renders the NPC state with the npc class and label', () => {
    const wrapper = mount(RoleBadge, { props: { role: 'NPC' } })
    expect(wrapper.classes()).toContain('npc')
    expect(wrapper.find('.label').text()).toBe('NPC')
    expect(wrapper.attributes('style')).toContain('npc-badge-art.png')
  })
})

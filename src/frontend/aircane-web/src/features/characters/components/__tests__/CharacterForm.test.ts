import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CharacterForm from '../CharacterForm.vue'
import { CharacterRole, type CharacterDto, type CreateCharacterRequest } from '../../api'

function makeCharacter(overrides: Partial<CharacterDto> = {}): CharacterDto {
  return {
    id: 'char-1',
    campaignId: null,
    campaignName: null,
    ownerParticipantId: null,
    role: CharacterRole.Player,
    name: 'Warduke',
    gameSystem: 'D&D 5e',
    ruleset: '2014',
    level: 3,
    canonicalJson: JSON.stringify({ classes: [{ className: 'Fighter', level: 3, hitDie: 10 }] }),
    currentStateJson: '{}',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  }
}

function roleButton(wrapper: ReturnType<typeof mount>, label: string) {
  const button = wrapper.findAll('[role="radio"]').find((b) => b.text() === label)
  if (!button) throw new Error(`No role option labelled ${label}`)
  return button
}

async function fillRequiredCreateFields(wrapper: ReturnType<typeof mount>) {
  await wrapper.find('#char-name').setValue('Aldric')
  await wrapper.find('#char-class').setValue('Fighter')
}

describe('CharacterForm role toggle', () => {
  it('defaults new characters to Player', async () => {
    const wrapper = mount(CharacterForm)
    expect(roleButton(wrapper, 'Player').attributes('aria-checked')).toBe('true')
    expect(roleButton(wrapper, 'NPC').attributes('aria-checked')).toBe('false')

    await fillRequiredCreateFields(wrapper)
    await wrapper.find('form').trigger('submit')

    const payload = wrapper.emitted('submit')![0][0] as CreateCharacterRequest
    expect(payload.role).toBe(CharacterRole.Player)
  })

  it('submits NPC when the NPC option is selected', async () => {
    const wrapper = mount(CharacterForm)
    await fillRequiredCreateFields(wrapper)
    await roleButton(wrapper, 'NPC').trigger('click')
    await wrapper.find('form').trigger('submit')

    const payload = wrapper.emitted('submit')![0][0] as CreateCharacterRequest
    expect(payload.role).toBe(CharacterRole.Npc)
  })

  it('pre-selects and updates the role of an existing character', async () => {
    const wrapper = mount(CharacterForm, { props: { character: makeCharacter({ role: CharacterRole.Npc }) } })
    expect(roleButton(wrapper, 'NPC').attributes('aria-checked')).toBe('true')

    await roleButton(wrapper, 'Player').trigger('click')
    await wrapper.find('form').trigger('submit')

    const payload = wrapper.emitted('submit')![0][0] as { role?: CharacterRole }
    expect(payload.role).toBe(CharacterRole.Player)
  })
})

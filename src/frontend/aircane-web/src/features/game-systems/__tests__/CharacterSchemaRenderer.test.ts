import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CharacterSchemaRenderer from '../components/CharacterSchemaRenderer.vue'
import type { FormDescriptor } from '../types'

const basicDescriptor: FormDescriptor = {
  sections: [
    {
      id: 'basics',
      label: 'Basic Information',
      fields: [
        { id: 'name', type: 'text', label: 'Character Name', required: true },
        { id: 'level', type: 'number', label: 'Level', required: true, min: 1, max: 20 },
        { id: 'class', type: 'enum', label: 'Class', required: false, options: ['Fighter', 'Wizard', 'Rogue'] },
        { id: 'is_npc', type: 'boolean', label: 'Is NPC', required: false },
      ],
    },
    {
      id: 'abilities',
      label: 'Ability Scores',
      fields: [
        { id: 'str', type: 'number', label: 'Strength', required: false, min: 1, max: 30 },
        { id: 'str_mod', type: 'calculated', label: 'STR Mod', required: false, formula: 'floor((str - 10) / 2)' },
      ],
    },
  ],
}

const conditionalDescriptor: FormDescriptor = {
  sections: [
    {
      id: 'basics',
      label: 'Basic Information',
      fields: [
        { id: 'class', type: 'enum', label: 'Class', required: true, options: ['Fighter', 'Wizard'] },
      ],
    },
    {
      id: 'spells',
      label: 'Spellcasting',
      visibleWhen: { field: 'class', in: ['Wizard'] },
      fields: [
        { id: 'spell_slots', type: 'number', label: 'Spell Slots', required: false },
      ],
    },
  ],
}

function mountRenderer(descriptor: FormDescriptor, modelValue: Record<string, unknown> = {}) {
  return mount(CharacterSchemaRenderer, {
    props: {
      descriptor,
      modelValue,
    },
  })
}

describe('CharacterSchemaRenderer', () => {
  describe('rendering', () => {
    it('renders all sections', () => {
      const wrapper = mountRenderer(basicDescriptor)
      expect(wrapper.text()).toContain('Basic Information')
      expect(wrapper.text()).toContain('Ability Scores')
    })

    it('renders text fields with correct labels', () => {
      const wrapper = mountRenderer(basicDescriptor)
      expect(wrapper.text()).toContain('Character Name')
    })

    it('renders number fields', () => {
      const wrapper = mountRenderer(basicDescriptor)
      const numberInput = wrapper.find('#field-level')
      expect(numberInput.exists()).toBe(true)
      expect(numberInput.attributes('type')).toBe('number')
    })

    it('renders enum fields as select', () => {
      const wrapper = mountRenderer(basicDescriptor)
      const select = wrapper.find('#field-class')
      expect(select.exists()).toBe(true)
      expect(select.element.tagName).toBe('SELECT')
    })

    it('renders enum options', () => {
      const wrapper = mountRenderer(basicDescriptor)
      const options = wrapper.findAll('#field-class option')
      // +1 for the "Select…" placeholder
      expect(options.length).toBe(4)
      expect(options[1].text()).toBe('Fighter')
      expect(options[2].text()).toBe('Wizard')
      expect(options[3].text()).toBe('Rogue')
    })

    it('renders boolean fields as checkbox', () => {
      const wrapper = mountRenderer(basicDescriptor)
      const checkbox = wrapper.find('input[type="checkbox"]')
      expect(checkbox.exists()).toBe(true)
    })

    it('renders calculated fields as disabled', () => {
      const wrapper = mountRenderer(basicDescriptor, { str_mod: 2 })
      const calcField = wrapper.find('#field-str_mod')
      expect(calcField.exists()).toBe(true)
      expect(calcField.attributes('disabled')).toBeDefined()
    })

    it('shows empty state when no sections', () => {
      const wrapper = mountRenderer({ sections: [] })
      expect(wrapper.text()).toContain('No character schema defined')
    })
  })

  describe('conditional visibility', () => {
    it('hides sections when condition is not met', () => {
      const wrapper = mountRenderer(conditionalDescriptor, { class: 'Fighter' })
      expect(wrapper.text()).not.toContain('Spellcasting')
    })

    it('shows sections when condition is met', () => {
      const wrapper = mountRenderer(conditionalDescriptor, { class: 'Wizard' })
      expect(wrapper.text()).toContain('Spellcasting')
    })
  })

  describe('interactions', () => {
    it('emits update:modelValue when text field changes', async () => {
      const wrapper = mountRenderer(basicDescriptor, { name: '' })
      const input = wrapper.find('#field-name')
      await input.setValue('Thorin')

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted).toBeTruthy()
      expect(emitted![0][0]).toEqual(expect.objectContaining({ name: 'Thorin' }))
    })

    it('emits update:modelValue when number field changes', async () => {
      const wrapper = mountRenderer(basicDescriptor, { level: 1 })
      const input = wrapper.find('#field-level')
      await input.setValue('5')

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted).toBeTruthy()
      expect(emitted![0][0]).toEqual(expect.objectContaining({ level: 5 }))
    })

    it('emits update:modelValue when enum field changes', async () => {
      const wrapper = mountRenderer(basicDescriptor, { class: '' })
      const select = wrapper.find('#field-class')
      await select.setValue('Wizard')

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted).toBeTruthy()
      expect(emitted![0][0]).toEqual(expect.objectContaining({ class: 'Wizard' }))
    })
  })

  describe('readonly mode', () => {
    it('disables all inputs when readonly', () => {
      const wrapper = mount(CharacterSchemaRenderer, {
        props: {
          descriptor: basicDescriptor,
          modelValue: {},
          readonly: true,
        },
      })
      const inputs = wrapper.findAll('input:not([type="checkbox"]), select, textarea')
      inputs.forEach((input) => {
        expect(input.attributes('disabled')).toBeDefined()
      })
    })
  })

  // ── BUG-1 regression: integer wire field types must still render controls ──────
  // The preview-character-form endpoint serializes field types as integer enum
  // values (0=Text, 1=Number, 3=Enum, 8=Calculated). A descriptor that reaches the
  // renderer un-normalized must still render the correct controls with seeded values.
  describe('integer wire field types (BUG-1 regression)', () => {
    // Build a descriptor whose field types are the raw integers from the wire.
    const integerWireDescriptor = {
      sections: [
        {
          id: 'basics',
          label: 'Basic Information',
          fields: [
            { id: 'name', type: 0, label: 'Character Name', required: true },
            { id: 'level', type: 1, label: 'Level', required: true, min: 1, max: 20 },
            { id: 'class', type: 3, label: 'Class', required: false, options: ['Fighter', 'Wizard', 'Rogue'] },
          ],
        },
        {
          id: 'abilities',
          label: 'Ability Scores',
          fields: [
            { id: 'str', type: 1, label: 'Strength', required: false, min: 1, max: 30 },
            { id: 'str_mod', type: 8, label: 'STR Mod', required: false, formula: 'floor((str - 10) / 2)' },
          ],
        },
      ],
    } as unknown as FormDescriptor

    it('renders controls and seeded values for an integer-wire descriptor', () => {
      const wrapper = mountRenderer(integerWireDescriptor, {
        name: 'Ruelin Treeshield',
        str: 8,
        level: 2,
        class: 'Wizard',
        str_mod: -1,
      })

      // Text field (type 0) renders a text input showing the seeded name.
      const nameInput = wrapper.find('#field-name')
      expect(nameInput.exists()).toBe(true)
      expect(nameInput.attributes('type')).toBe('text')
      expect((nameInput.element as HTMLInputElement).value).toBe('Ruelin Treeshield')

      // Number field (type 1) renders a number input showing the seeded strength.
      const strInput = wrapper.find('#field-str')
      expect(strInput.exists()).toBe(true)
      expect(strInput.attributes('type')).toBe('number')
      expect((strInput.element as HTMLInputElement).value).toBe('8')

      // Enum field (type 3) renders a select whose value is the seeded class.
      const classSelect = wrapper.find('#field-class')
      expect(classSelect.exists()).toBe(true)
      expect(classSelect.element.tagName).toBe('SELECT')
      expect((classSelect.element as HTMLSelectElement).value).toBe('Wizard')

      // Calculated field (type 8) renders a disabled input.
      const calcInput = wrapper.find('#field-str_mod')
      expect(calcInput.exists()).toBe(true)
      expect(calcInput.attributes('disabled')).toBeDefined()
    })
  })
})

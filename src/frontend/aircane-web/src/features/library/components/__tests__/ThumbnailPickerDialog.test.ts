import { afterEach, describe, expect, it } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import ThumbnailPickerDialog from '../ThumbnailPickerDialog.vue'

let wrapper: VueWrapper | null = null

function mountDialog(props: Record<string, unknown> = {}) {
  wrapper = mount(ThumbnailPickerDialog, {
    props: { open: true, currentKey: null, autoKey: 'fantasy-rulebook-dd', ...props },
    global: { stubs: { Teleport: true } },
    attachTo: document.body,
  })
  return wrapper
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = null
})

const tick = async (w: VueWrapper) => {
  await w.vm.$nextTick()
  await w.vm.$nextTick()
}

describe('ThumbnailPickerDialog', () => {
  it('renders nothing while closed', () => {
    const w = mountDialog({ open: false })
    expect(w.find('[role="dialog"]').exists()).toBe(false)
  })

  it('is a modal dialog labelled by its title', () => {
    const w = mountDialog({ title: 'Change thumbnail', subtitle: 'The Book' })
    const dialog = w.get('[role="dialog"]')
    expect(dialog.attributes('aria-modal')).toBe('true')
    expect(w.get(`#${dialog.attributes('aria-labelledby')}`).text()).toBe('Change thumbnail')
    expect(w.text()).toContain('The Book')
  })

  it('opens with the current thumbnail selected and focused', async () => {
    const w = mountDialog({ currentKey: 'space-opera-sw' })
    await tick(w)
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('data-option')).toBe('space-opera-sw')
    expect(document.activeElement?.getAttribute('data-option')).toBe('space-opera-sw')
  })

  it('selects Automatic when there is no stored key', async () => {
    const w = mountDialog({ currentKey: null })
    await tick(w)
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('aria-label')).toBe('Automatic')
  })

  it('says the thumbnail is no longer available, and shows Automatic selected, when the stored key has no image', async () => {
    const w = mountDialog({ currentKey: 'removed-asset' })
    await tick(w)
    expect(w.get('[role="status"]').text()).toContain('This thumbnail is no longer available')
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('aria-label')).toBe('Automatic')
  })

  it('applies the chosen thumbnail and closes', async () => {
    const w = mountDialog()
    await w.get('[data-option="cosmic-horror-cc"]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
    expect(w.emitted('select')).toEqual([['cosmic-horror-cc']])
    expect(w.emitted('close')).toHaveLength(1)
  })

  it('applies Automatic as null', async () => {
    const w = mountDialog({ currentKey: 'space-opera-sw' })
    await w.get('[data-option=""]').trigger('click')
    await w.get('[data-action="apply"]').trigger('click')
    expect(w.emitted('select')).toEqual([[null]])
  })

  it('changes nothing on Cancel, the close button, the backdrop or Escape', async () => {
    const w = mountDialog()
    await w.get('[data-option="cosmic-horror-cc"]').trigger('click')

    await w.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
    await w.get('[aria-label="Close"]').trigger('click')
    await w.get('[role="dialog"]').trigger('keydown', { key: 'Escape' })
    await w.get('.fixed.inset-0').trigger('click')

    expect(w.emitted('close')).toHaveLength(4)
    expect(w.emitted('select')).toBeUndefined()
  })

  it('does not let Escape reach a dialog underneath', async () => {
    const w = mountDialog()
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    let bubbled = false
    document.body.addEventListener('keydown', () => { bubbled = true }, { once: true })
    w.get('[role="dialog"]').element.dispatchEvent(event)
    expect(bubbled).toBe(false)
  })

  it('keeps Tab inside the dialog: from the last control it wraps to the first, and Shift+Tab the other way', async () => {
    const w = mountDialog()
    await tick(w)
    const dialog = w.get('[role="dialog"]')
    const focusable = [...dialog.element.querySelectorAll<HTMLElement>('button, [tabindex]')].filter((el) => el.tabIndex >= 0)
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    expect(first.getAttribute('aria-label')).toBe('Close')
    expect(last.getAttribute('data-action')).toBe('apply')

    last.focus()
    await dialog.trigger('keydown', { key: 'Tab' })
    expect(document.activeElement).toBe(first)

    first.focus()
    await dialog.trigger('keydown', { key: 'Tab', shiftKey: true })
    expect(document.activeElement).toBe(last)
  })

  it('returns focus to the control that opened it', async () => {
    const opener = document.createElement('button')
    document.body.appendChild(opener)
    opener.focus()
    const w = mountDialog({ open: false })
    await w.setProps({ open: true })
    await tick(w)
    expect(document.activeElement).not.toBe(opener)
    await w.setProps({ open: false })
    expect(document.activeElement).toBe(opener)
    opener.remove()
  })

  it('starts again from the stored key each time it opens', async () => {
    const w = mountDialog({ currentKey: 'space-opera-sw' })
    await w.get('[data-option="cosmic-horror-cc"]').trigger('click')
    await w.setProps({ open: false })
    await w.setProps({ open: true })
    expect(w.get('[role="radio"][aria-checked="true"]').attributes('data-option')).toBe('space-opera-sw')
  })

  it('has 44 px buttons', () => {
    const w = mountDialog()
    for (const b of w.findAll('[role="dialog"] > div:first-child button, [data-action="apply"]')) {
      expect(b.classes().some((c) => c.startsWith('min-h-[44px]') || c === 'h-11')).toBe(true)
    }
  })
})

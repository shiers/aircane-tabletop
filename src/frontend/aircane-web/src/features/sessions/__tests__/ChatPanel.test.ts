import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ChatPanel from '../ChatPanel.vue'
import type { ChatMessage } from '../ChatPanel.vue'

const mockMessages: ChatMessage[] = [
  {
    id: 'msg-1',
    senderName: 'Thorin',
    text: 'I am ready for battle!',
    timestamp: '2024-01-01T10:00:00Z',
    isOwn: false,
  },
  {
    id: 'msg-2',
    senderName: 'You',
    text: 'Let us proceed.',
    timestamp: '2024-01-01T10:01:00Z',
    isOwn: true,
  },
]

function mountPanel(overrides: Partial<{
  messages: ChatMessage[]
  disabled: boolean
  placeholder: string
}> = {}) {
  return mount(ChatPanel, {
    props: {
      messages: mockMessages,
      disabled: false,
      ...overrides,
    },
  })
}

describe('ChatPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ---------------------------------------------------------------------------
  // Rendering
  // ---------------------------------------------------------------------------

  it('renders the chat heading', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Chat')
  })

  it('renders all messages', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('I am ready for battle!')
    expect(wrapper.text()).toContain('Let us proceed.')
  })

  it('renders sender names', () => {
    const wrapper = mountPanel()
    expect(wrapper.text()).toContain('Thorin')
    expect(wrapper.text()).toContain('You')
  })

  it('shows empty state when there are no messages', () => {
    const wrapper = mountPanel({ messages: [] })
    expect(wrapper.text()).toContain('No messages yet.')
  })

  it('renders the message input field', () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    expect(input.exists()).toBe(true)
  })

  it('renders the send button', () => {
    const wrapper = mountPanel()
    const button = wrapper.find('button[aria-label="Send message"]')
    expect(button.exists()).toBe(true)
  })

  it('uses the custom placeholder when provided', () => {
    const wrapper = mountPanel({ placeholder: 'Message players…' })
    const input = wrapper.find('#chat-input')
    expect(input.attributes('placeholder')).toBe('Message players…')
  })

  it('uses the default placeholder when none is provided', () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    expect(input.attributes('placeholder')).toBe('Type a message…')
  })

  // ---------------------------------------------------------------------------
  // Accessibility
  // ---------------------------------------------------------------------------

  it('has a role="log" on the message list', () => {
    const wrapper = mountPanel()
    const log = wrapper.find('[role="log"]')
    expect(log.exists()).toBe(true)
  })

  it('has aria-live="polite" on the message list', () => {
    const wrapper = mountPanel()
    const log = wrapper.find('[role="log"]')
    expect(log.attributes('aria-live')).toBe('polite')
  })

  it('has a screen-reader label for the input', () => {
    const wrapper = mountPanel()
    const label = wrapper.find('label[for="chat-input"]')
    expect(label.exists()).toBe(true)
    expect(label.classes()).toContain('sr-only')
  })

  // ---------------------------------------------------------------------------
  // Disabled state
  // ---------------------------------------------------------------------------

  it('disables the input when disabled prop is true', () => {
    const wrapper = mountPanel({ disabled: true })
    const input = wrapper.find('#chat-input')
    expect(input.attributes('disabled')).toBeDefined()
  })

  it('disables the send button when disabled prop is true', () => {
    const wrapper = mountPanel({ disabled: true })
    const button = wrapper.find('button[aria-label="Send message"]')
    expect(button.attributes('disabled')).toBeDefined()
  })

  // ---------------------------------------------------------------------------
  // Interactions
  // ---------------------------------------------------------------------------

  it('emits send event with the message text when Send is clicked', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('Hello world')
    const button = wrapper.find('button[aria-label="Send message"]')
    await button.trigger('click')

    expect(wrapper.emitted('send')).toBeTruthy()
    expect(wrapper.emitted('send')![0]).toEqual(['Hello world'])
  })

  it('emits send event when Enter is pressed in the input', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('Enter key message')
    await input.trigger('keydown', { key: 'Enter', shiftKey: false })

    expect(wrapper.emitted('send')).toBeTruthy()
    expect(wrapper.emitted('send')![0]).toEqual(['Enter key message'])
  })

  it('does not emit send when Shift+Enter is pressed', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('Multiline')
    await input.trigger('keydown', { key: 'Enter', shiftKey: true })

    expect(wrapper.emitted('send')).toBeFalsy()
  })

  it('clears the input after sending', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('Test message')
    const button = wrapper.find('button[aria-label="Send message"]')
    await button.trigger('click')

    expect((input.element as HTMLInputElement).value).toBe('')
  })

  it('does not emit send when the input is empty', async () => {
    const wrapper = mountPanel()
    const button = wrapper.find('button[aria-label="Send message"]')
    await button.trigger('click')

    expect(wrapper.emitted('send')).toBeFalsy()
  })

  it('does not emit send when the input contains only whitespace', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('   ')
    const button = wrapper.find('button[aria-label="Send message"]')
    await button.trigger('click')

    expect(wrapper.emitted('send')).toBeFalsy()
  })

  it('trims whitespace from the message before emitting', async () => {
    const wrapper = mountPanel()
    const input = wrapper.find('#chat-input')
    await input.setValue('  hello  ')
    const button = wrapper.find('button[aria-label="Send message"]')
    await button.trigger('click')

    expect(wrapper.emitted('send')![0]).toEqual(['hello'])
  })
})

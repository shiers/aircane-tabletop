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

describe('ChatPanel: history paging', () => {
  const mk = (id: string, minute: number): ChatMessage => ({
    id,
    senderName: 'Bifur',
    text: `line ${id}`,
    timestamp: `2024-01-01T10:${String(minute).padStart(2, '0')}:00Z`,
  })

  it('offers "Load earlier messages" when older messages exist, and asks for them when pressed', async () => {
    const wrapper = mount(ChatPanel, { props: { messages: [mk('b', 2)], hasMore: true } })

    const button = wrapper.find('[data-testid="load-older"]')
    expect(button.text()).toBe('Load earlier messages')
    await button.trigger('click')

    expect(wrapper.emitted('load-older')).toHaveLength(1)
  })

  it('has no such button when there is nothing older', () => {
    const wrapper = mount(ChatPanel, { props: { messages: [mk('b', 2)], hasMore: false } })

    expect(wrapper.find('[data-testid="load-older"]').exists()).toBe(false)
  })

  it('shows "Loading…" and disables the button while the older page loads', () => {
    const wrapper = mount(ChatPanel, { props: { messages: [mk('b', 2)], hasMore: true, loadingOlder: true } })

    const button = wrapper.find('[data-testid="load-older"]')
    expect(button.text()).toBe('Loading…')
    expect(button.attributes('disabled')).toBeDefined()
  })

  it('says when the history could not be loaded', () => {
    const wrapper = mount(ChatPanel, { props: { messages: [], historyError: 'Could not load the chat history.' } })

    expect(wrapper.find('[data-testid="chat-history-error"]').text()).toBe('Could not load the chat history.')
  })

  it('scrolls to the newest message when one arrives at the bottom', async () => {
    const wrapper = mount(ChatPanel, { props: { messages: [mk('a', 1), mk('b', 2)] }, attachTo: document.body })
    const list = wrapper.find('[role="log"]').element as HTMLElement
    Object.defineProperty(list, 'scrollHeight', { configurable: true, value: 900 })
    Object.defineProperty(list, 'clientHeight', { configurable: true, value: 300 })
    list.scrollTop = 0

    await wrapper.setProps({ messages: [mk('a', 1), mk('b', 2), mk('c', 3)] })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(list.scrollTop).toBe(900)
    wrapper.unmount()
  })

  it('keeps the reader\'s place when older messages are added at the top (it does not jump to the bottom)', async () => {
    const wrapper = mount(ChatPanel, { props: { messages: [mk('c', 3), mk('d', 4)], hasMore: true }, attachTo: document.body })
    const list = wrapper.find('[role="log"]').element as HTMLElement
    // The content is as tall as the messages in it (100px each), like a real list.
    Object.defineProperty(list, 'scrollHeight', {
      configurable: true,
      get: () => 100 + list.querySelectorAll(':scope > div.flex').length * 100,
    })
    Object.defineProperty(list, 'clientHeight', { configurable: true, value: 300 })
    list.scrollTop = 0   // at the top: they pressed "Load earlier messages"

    // Two older messages are added above: the content grows by 200px once rendered.
    await wrapper.setProps({ messages: [mk('a', 1), mk('b', 2), mk('c', 3), mk('d', 4)] })
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(list.scrollTop).toBe(200)   // still looking at what they were looking at
    wrapper.unmount()
  })
})

describe('ChatPanel: the Send button stays inside the card', () => {
  it('lets the input shrink and never the button, so the row cannot overflow a narrow card', () => {
    const wrapper = mount(ChatPanel, { props: { messages: [] } })

    expect(wrapper.find('#chat-input').classes()).toEqual(expect.arrayContaining(['min-w-0', 'flex-1']))
    const send = wrapper.find('button[aria-label="Send message"]')
    expect(send.classes()).toContain('shrink-0')
    expect(send.classes()).toContain('min-h-[44px]')
    expect(wrapper.find('section').classes()).toContain('min-w-0')
  })

  it('uses 16px input text so a phone does not zoom the page when it is focused', () => {
    const wrapper = mount(ChatPanel, { props: { messages: [] } })

    expect(wrapper.find('#chat-input').classes()).toContain('text-base')
  })
})

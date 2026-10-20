import { describe, it, expect, vi } from 'vitest'
import type { ChatHistoryMessage, ChatHistoryPage } from '../api'
import { ParticipantRole } from '../api'
import { fromHistory, fromLiveEvent, mergeChat, useChatHistory } from '../chatHistory'

function stored(id: string, minute: number, overrides: Partial<ChatHistoryMessage> = {}): ChatHistoryMessage {
  return {
    id,
    sessionId: 'session-1',
    senderId: 'bifur',
    senderName: 'Bifur',
    senderRole: ParticipantRole.Player,
    text: `message ${id}`,
    sentAt: `2026-10-20T10:${String(minute).padStart(2, '0')}:00Z`,
    ...overrides,
  }
}

const isMe = (senderId: string) => senderId === 'thorin'

describe('turning messages into chat lines', () => {
  it('uses the stored id for a history message, and marks the viewer\'s own', () => {
    const line = fromHistory(stored('m1', 1, { senderId: 'thorin', senderName: 'Thorin' }), isMe)

    expect(line).toMatchObject({ id: 'm1', senderName: 'Thorin', text: 'message m1', isOwn: true })
    expect(fromHistory(stored('m2', 2), isMe).isOwn).toBe(false)
  })

  it('uses the stored id from a live event too, so the same message is not shown twice', () => {
    const live = fromLiveEvent(
      { sessionId: 'session-1', participantId: 'bifur', displayName: 'Bifur', text: 'hello', sentAt: '2026-10-20T10:01:00Z', messageId: 'm1' },
      isMe,
    )

    expect(live.id).toBe('m1')
  })

  it('gives a live message from a server without stored chat an id of its own', () => {
    const live = fromLiveEvent(
      { sessionId: 'session-1', participantId: 'bifur', displayName: 'Bifur', text: 'hello', sentAt: '2026-10-20T10:01:00Z' },
      isMe,
    )

    expect(live.id).toBe('bifur-2026-10-20T10:01:00Z')
  })
})

describe('mergeChat', () => {
  it('shows each message once and keeps time order', () => {
    const a = fromHistory(stored('a', 1), isMe)
    const b = fromHistory(stored('b', 2), isMe)
    const c = fromHistory(stored('c', 3), isMe)

    expect(mergeChat([b, c], [a, b]).map((m) => m.id)).toEqual(['a', 'b', 'c'])
  })

  it('keeps the given order for messages with the same time', () => {
    const first = fromHistory(stored('x', 5), isMe)
    const second = fromHistory(stored('y', 5), isMe)

    expect(mergeChat([first], [second]).map((m) => m.id)).toEqual(['x', 'y'])
  })
})

describe('useChatHistory', () => {
  function page(messages: ChatHistoryMessage[], hasMore = false): ChatHistoryPage {
    return { messages, hasMore }
  }

  it('loads the newest page', async () => {
    const fetchPage = vi.fn().mockResolvedValue(page([stored('a', 1), stored('b', 2)], true))
    const chat = useChatHistory('session-1', isMe, fetchPage)

    await chat.refresh()

    expect(fetchPage).toHaveBeenCalledWith('session-1')
    expect(chat.messages.value.map((m) => m.id)).toEqual(['a', 'b'])
    expect(chat.hasMore.value).toBe(true)
  })

  it('merges history with live messages without duplicates, whichever arrives first', async () => {
    const chat = useChatHistory('session-1', isMe, vi.fn().mockResolvedValue(page([stored('a', 1), stored('b', 2)])))

    // b arrives live before the history loads; the history then has it too; then c arrives live.
    chat.addLive({ sessionId: 's', participantId: 'bifur', displayName: 'Bifur', text: 'message b', sentAt: stored('b', 2).sentAt, messageId: 'b' })
    await chat.refresh()
    chat.addLive({ sessionId: 's', participantId: 'bifur', displayName: 'Bifur', text: 'message c', sentAt: stored('c', 3).sentAt, messageId: 'c' })

    expect(chat.messages.value.map((m) => m.id)).toEqual(['a', 'b', 'c'])
  })

  it('pages up with before set to the oldest stored message, and puts the older page on top', async () => {
    const fetchPage = vi.fn()
      .mockResolvedValueOnce(page([stored('d', 4), stored('e', 5)], true))
      .mockResolvedValueOnce(page([stored('b', 2), stored('c', 3)], true))
      .mockResolvedValueOnce(page([stored('a', 1)], false))
    const chat = useChatHistory('session-1', isMe, fetchPage)
    await chat.refresh()

    await chat.loadOlder()
    expect(fetchPage).toHaveBeenLastCalledWith('session-1', { before: 'd' })
    expect(chat.messages.value.map((m) => m.id)).toEqual(['b', 'c', 'd', 'e'])
    expect(chat.hasMore.value).toBe(true)

    await chat.loadOlder()
    expect(fetchPage).toHaveBeenLastCalledWith('session-1', { before: 'b' })
    expect(chat.messages.value.map((m) => m.id)).toEqual(['a', 'b', 'c', 'd', 'e'])
    expect(chat.hasMore.value).toBe(false)
  })

  it('does not ask for older messages when there are none, or while one request is in flight', async () => {
    let release: (value: ChatHistoryPage) => void = () => undefined
    const fetchPage = vi.fn()
      .mockResolvedValueOnce(page([stored('d', 4)], true))
      .mockReturnValueOnce(new Promise<ChatHistoryPage>((resolve) => (release = resolve)))
    const chat = useChatHistory('session-1', isMe, fetchPage)
    await chat.refresh()

    const first = chat.loadOlder()
    await chat.loadOlder()   // already loading
    expect(fetchPage).toHaveBeenCalledTimes(2)
    release(page([stored('c', 3)], false))
    await first

    await chat.loadOlder()   // nothing older
    expect(fetchPage).toHaveBeenCalledTimes(2)
  })

  it('a later refresh (the chat tab opened, the connection came back) adds what was missed and keeps the paged-up state', async () => {
    const fetchPage = vi.fn()
      .mockResolvedValueOnce(page([stored('d', 4), stored('e', 5)], true))
      .mockResolvedValueOnce(page([stored('a', 1), stored('b', 2), stored('c', 3)], false))
      .mockResolvedValueOnce(page([stored('d', 4), stored('e', 5), stored('f', 6)], true))
    const chat = useChatHistory('session-1', isMe, fetchPage)
    await chat.refresh()
    await chat.loadOlder()
    expect(chat.hasMore.value).toBe(false)

    await chat.refresh()

    expect(chat.messages.value.map((m) => m.id)).toEqual(['a', 'b', 'c', 'd', 'e', 'f'])
    expect(chat.hasMore.value).toBe(false)   // the newest page says "more", but they have paged to the start
  })

  it('keeps what is on screen and says so when the history cannot be loaded', async () => {
    const chat = useChatHistory('session-1', isMe, vi.fn().mockRejectedValue(new Error('offline')))
    chat.addLive({ sessionId: 's', participantId: 'bifur', displayName: 'Bifur', text: 'hi', sentAt: stored('a', 1).sentAt, messageId: 'a' })

    await chat.refresh()

    expect(chat.messages.value).toHaveLength(1)
    expect(chat.error.value).toContain('chat history')
  })
})

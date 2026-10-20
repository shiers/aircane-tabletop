import { computed, ref } from 'vue'
import { getChatHistory, type ChatHistoryMessage, type ChatHistoryPage } from './api'
import type { ChatMessage } from './ChatPanel.vue'
import type { ChatMessageEvent } from './playerHub'

/**
 * The table chat as a screen shows it: the stored history (GET /api/sessions/{id}/chat) merged with the
 * messages that arrive live over the hub.
 *
 * The same message can come from both: the hub sends it as it is said, and the history has it once the screen
 * (re)loads. They carry the same id (the live event's `messageId` is the stored message's id), so merging by id
 * shows each once. A live message from a server that predates stored chat has no `messageId`; it gets an id of
 * its own and can show twice next to history, which is no worse than before history existed.
 */

/** A stored message as the chat panel shows it. */
export function fromHistory(message: ChatHistoryMessage, isOwn: (senderId: string) => boolean): ChatMessage {
  return {
    id: message.id,
    senderName: message.senderName,
    text: message.text,
    timestamp: message.sentAt,
    isOwn: isOwn(message.senderId),
  }
}

/** A live `ChatMessageReceived` as the chat panel shows it. */
export function fromLiveEvent(event: ChatMessageEvent, isOwn: (senderId: string) => boolean): ChatMessage {
  return {
    id: event.messageId ?? `${event.participantId}-${event.sentAt}`,
    senderName: event.displayName,
    text: event.text,
    timestamp: event.sentAt,
    isOwn: isOwn(event.participantId),
  }
}

/**
 * Merges two lists of messages without duplicates (by id; the first occurrence's content wins), oldest
 * first. Messages with the same time keep the order they were given in.
 */
export function mergeChat(existing: readonly ChatMessage[], incoming: readonly ChatMessage[]): ChatMessage[] {
  const seen = new Set<string>()
  const merged: ChatMessage[] = []
  for (const message of [...existing, ...incoming]) {
    if (seen.has(message.id)) continue
    seen.add(message.id)
    merged.push(message)
  }
  return merged
    .map((message, index) => ({ message, index, time: Date.parse(message.timestamp) }))
    .sort((a, b) => (a.time - b.time) || (a.index - b.index))
    .map((entry) => entry.message)
}

/**
 * Chat history state for one screen.
 *
 * @param sessionId the play session
 * @param isOwn whether a sender id is the viewer (to put their messages on the right)
 * @param fetchPage the history call (replaceable for tests)
 */
export function useChatHistory(
  sessionId: string,
  isOwn: (senderId: string) => boolean,
  fetchPage: (sessionId: string, options?: { limit?: number; before?: string }) => Promise<ChatHistoryPage> = getChatHistory,
) {
  const messages = ref<ChatMessage[]>([])
  const hasMore = ref(false)
  const loadingOlder = ref(false)
  const error = ref<string | null>(null)

  // The oldest message that came from history: the cursor for paging up. A live message cannot be one,
  // because only history messages are known to be stored (and in order).
  const storedTimes = new Map<string, number>()
  let oldestStoredId: string | null = null
  let pagedUp = false

  function remember(page: ChatHistoryPage): void {
    for (const m of page.messages) storedTimes.set(m.id, Date.parse(m.sentAt))
    let oldest: { id: string; time: number } | null = null
    for (const [id, time] of storedTimes) {
      if (oldest === null || time < oldest.time) oldest = { id, time }
    }
    oldestStoredId = oldest?.id ?? null
  }

  /**
   * Loads the newest page and merges it with what is on screen. Called when the chat opens and after a
   * reconnect, when messages may have been missed. Once the player has paged up, the "older" state is theirs
   * and a refresh leaves it alone.
   */
  async function refresh(): Promise<void> {
    try {
      const page = await fetchPage(sessionId)
      messages.value = mergeChat(messages.value, page.messages.map((m) => fromHistory(m, isOwn)))
      remember(page)
      if (!pagedUp) hasMore.value = page.hasMore
      error.value = null
    } catch {
      error.value = 'Could not load the chat history.'
    }
  }

  /** Loads the page before the oldest stored message and adds it to the top. */
  async function loadOlder(): Promise<void> {
    if (!hasMore.value || loadingOlder.value || oldestStoredId === null) return
    loadingOlder.value = true
    try {
      const page = await fetchPage(sessionId, { before: oldestStoredId })
      messages.value = mergeChat(messages.value, page.messages.map((m) => fromHistory(m, isOwn)))
      remember(page)
      pagedUp = true
      hasMore.value = page.hasMore
      error.value = null
    } catch {
      error.value = 'Could not load earlier messages.'
    } finally {
      loadingOlder.value = false
    }
  }

  /** Adds a message that arrived live. */
  function addLive(event: ChatMessageEvent): void {
    messages.value = mergeChat(messages.value, [fromLiveEvent(event, isOwn)])
  }

  const count = computed(() => messages.value.length)

  return { messages, hasMore, loadingOlder, error, count, refresh, loadOlder, addLive }
}

<script setup lang="ts">
/**
 * ChatPanel - shared chat component for both host and player session screens.
 * Displays a scrollable message log and an input field to send new messages.
 */
import { ref, nextTick, watch } from 'vue'

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface ChatMessage {
  id: string
  senderName: string
  text: string
  timestamp: string
  /** True when this message was sent by the current user. */
  isOwn?: boolean
}

// ---------------------------------------------------------------------------
// Props & Emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  messages: ChatMessage[]
  /** Whether the send button/input should be disabled (e.g. not connected). */
  disabled?: boolean
  placeholder?: string
  /** Older messages exist on the server: show "Load earlier messages". */
  hasMore?: boolean
  /** Older messages are being loaded. */
  loadingOlder?: boolean
  /** Why the history could not be loaded, if it could not. */
  historyError?: string | null
}>()

const emit = defineEmits<{
  (e: 'send', text: string): void
  (e: 'load-older'): void
}>()

// ---------------------------------------------------------------------------
// Input state
// ---------------------------------------------------------------------------

const inputText = ref('')
const messageListRef = ref<HTMLElement | null>(null)

// ---------------------------------------------------------------------------
// Scrolling: a new message at the bottom scrolls to it; older messages added at the top must not move the
// reader (the view keeps the message they were looking at where it was).
// ---------------------------------------------------------------------------

watch(
  () => props.messages[props.messages.length - 1]?.id,
  async () => {
    await nextTick()
    if (messageListRef.value) {
      messageListRef.value.scrollTop = messageListRef.value.scrollHeight
    }
  },
)

// Runs before the DOM updates, so the old height is still readable.
watch(
  () => props.messages[0]?.id,
  async (newFirst, oldFirst) => {
    const list = messageListRef.value
    if (!list || oldFirst === undefined || newFirst === oldFirst) return
    const before = list.scrollHeight
    await nextTick()
    // Only when messages were added above (the last one did not change), keep the reader's place.
    if (list.scrollTop < list.scrollHeight - list.clientHeight) list.scrollTop += list.scrollHeight - before
  },
)

// ---------------------------------------------------------------------------
// Send
// ---------------------------------------------------------------------------

function handleSend(): void {
  const text = inputText.value.trim()
  if (!text || props.disabled) return
  emit('send', text)
  inputText.value = ''
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    handleSend()
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatTime(timestamp: string): string {
  try {
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}
</script>

<template>
  <section aria-labelledby="chat-panel-heading" class="flex h-full min-w-0 flex-col">
    <h3 id="chat-panel-heading" class="mb-3 text-sm font-medium uppercase tracking-wider text-gray-400">
      Chat
    </h3>

    <!-- Message list -->
    <div
      ref="messageListRef"
      role="log"
      aria-label="Chat messages"
      aria-live="polite"
      class="flex-1 overflow-y-auto space-y-2 min-h-0 pr-1"
    >
      <button
        v-if="hasMore"
        type="button"
        :disabled="loadingOlder"
        class="mx-auto block min-h-[44px] rounded-md border border-gray-700 px-3 py-2 text-xs text-gray-300 hover:bg-gray-800 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50"
        data-testid="load-older"
        @click="emit('load-older')"
      >
        {{ loadingOlder ? 'Loading…' : 'Load earlier messages' }}
      </button>
      <p v-if="historyError" role="status" class="text-center text-xs text-orange-300" data-testid="chat-history-error">
        {{ historyError }}
      </p>

      <p v-if="messages.length === 0" class="text-xs text-gray-600 text-center py-4">
        No messages yet.
      </p>

      <div
        v-for="message in messages"
        :key="message.id"
        :class="[
          'flex flex-col gap-0.5',
          message.isOwn ? 'items-end' : 'items-start',
        ]"
      >
        <!-- Sender + time -->
        <div class="flex items-baseline gap-2 px-1">
          <span class="text-xs font-medium text-gray-400">{{ message.senderName }}</span>
          <time
            :datetime="message.timestamp"
            class="text-xs text-gray-600"
          >
            {{ formatTime(message.timestamp) }}
          </time>
        </div>

        <!-- Bubble -->
        <div
          :class="[
            'max-w-[85%] rounded-lg px-3 py-2 text-sm break-words sm:max-w-xs',
            message.isOwn
              ? 'bg-aircane-700 text-white rounded-tr-none'
              : 'bg-gray-800 text-gray-100 rounded-tl-none',
          ]"
        >
          {{ message.text }}
        </div>
      </div>
    </div>

    <!-- Input -->
    <div class="mt-3 flex min-w-0 gap-2">
      <label for="chat-input" class="sr-only">Message</label>
      <input
        id="chat-input"
        v-model="inputText"
        type="text"
        :placeholder="placeholder ?? 'Type a message…'"
        :disabled="disabled"
        maxlength="500"
        class="min-h-[44px] min-w-0 flex-1 rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-base text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-50"
        @keydown="handleKeydown"
      />
      <button
        type="button"
        :disabled="disabled || !inputText.trim()"
        class="min-h-[44px] shrink-0 rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
        aria-label="Send message"
        @click="handleSend"
      >
        Send
      </button>
    </div>
  </section>
</template>

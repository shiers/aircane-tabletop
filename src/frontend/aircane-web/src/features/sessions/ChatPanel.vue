<script setup lang="ts">
/**
 * ChatPanel — shared chat component for both host and player session screens.
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
}>()

const emit = defineEmits<{
  (e: 'send', text: string): void
}>()

// ---------------------------------------------------------------------------
// Input state
// ---------------------------------------------------------------------------

const inputText = ref('')
const messageListRef = ref<HTMLElement | null>(null)

// ---------------------------------------------------------------------------
// Auto-scroll to bottom when new messages arrive
// ---------------------------------------------------------------------------

watch(
  () => props.messages.length,
  async () => {
    await nextTick()
    if (messageListRef.value) {
      messageListRef.value.scrollTop = messageListRef.value.scrollHeight
    }
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
  <section aria-labelledby="chat-panel-heading" class="flex flex-col h-full">
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
            'max-w-xs rounded-lg px-3 py-2 text-sm break-words',
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
    <div class="mt-3 flex gap-2">
      <label for="chat-input" class="sr-only">Message</label>
      <input
        id="chat-input"
        v-model="inputText"
        type="text"
        :placeholder="placeholder ?? 'Type a message…'"
        :disabled="disabled"
        maxlength="500"
        class="flex-1 rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-50"
        @keydown="handleKeydown"
      />
      <button
        type="button"
        :disabled="disabled || !inputText.trim()"
        class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
        aria-label="Send message"
        @click="handleSend"
      >
        Send
      </button>
    </div>
  </section>
</template>

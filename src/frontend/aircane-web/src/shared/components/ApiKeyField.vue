<script setup lang="ts">
import { ref } from 'vue'

// A masked API-key input with an eye toggle to reveal/hide the value.
// SECURITY: this component NEVER logs its value and never emits anything other
// than the user's own keystrokes. It stays compatible with the view's
// isKeyMasked() check — it does not transform or re-emit the bound value, so a
// masked placeholder loaded from the server is not re-sent as a real key.
withDefaults(
  defineProps<{
    modelValue: string
    placeholder?: string
    id?: string
  }>(),
  {
    placeholder: 'sk-...',
    id: undefined,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

const revealed = ref(false)

function onInput(event: Event) {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}

function toggleReveal() {
  revealed.value = !revealed.value
}
</script>

<template>
  <div class="relative">
    <input
      :id="id"
      :value="modelValue"
      :type="revealed ? 'text' : 'password'"
      :placeholder="placeholder"
      autocomplete="off"
      class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 pr-10 text-gray-100 focus:ring-2 focus:ring-aircane-500"
      @input="onInput"
    />
    <button
      type="button"
      class="absolute inset-y-0 right-0 flex items-center px-3 text-gray-400 hover:text-gray-200 focus:outline-none focus:text-aircane-400"
      :aria-label="revealed ? 'Hide API key' : 'Show API key'"
      :aria-pressed="revealed"
      @click="toggleReveal"
    >
      <!-- Eye (revealed) / eye-off (hidden) icon -->
      <svg
        v-if="revealed"
        xmlns="http://www.w3.org/2000/svg"
        class="h-5 w-5"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z" />
        <circle cx="12" cy="12" r="3" />
      </svg>
      <svg
        v-else
        xmlns="http://www.w3.org/2000/svg"
        class="h-5 w-5"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M9.9 4.24A9.1 9.1 0 0 1 12 4c6.5 0 10 7 10 7a13.2 13.2 0 0 1-2.16 2.95" />
        <path d="M6.6 6.6A13.2 13.2 0 0 0 2 12s3.5 7 10 7a9.1 9.1 0 0 0 3.42-.66" />
        <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
        <path d="m2 2 20 20" />
      </svg>
    </button>
  </div>
</template>

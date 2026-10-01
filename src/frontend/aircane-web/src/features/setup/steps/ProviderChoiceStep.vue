<script setup lang="ts">
/**
 * Step 2 — Choose AI provider. Selectable cards; clicking a card selects it,
 * clicking again (or Next) proceeds. Ollama -> step 3a, OpenAI -> step 3b,
 * "Try first" -> straight to Done.
 */
import { ref } from 'vue'
import type { SelectedProvider } from '../composables/useSetupWizard'

const props = defineProps<{
  modelValue: SelectedProvider
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SelectedProvider): void
  (e: 'back'): void
  (e: 'choose', value: Exclude<SelectedProvider, null>): void
}>()

const selected = ref<SelectedProvider>(props.modelValue)

type Option = {
  id: Exclude<SelectedProvider, null>
  icon: string
  title: string
  recommended?: boolean
  lines: string[]
}

const options: Option[] = [
  {
    id: 'ollama',
    icon: '🖥',
    title: 'Ollama',
    recommended: true,
    lines: [
      'Run AI locally on your machine.',
      'Free, private, no API key needed.',
      'Requires a separate Ollama install.',
    ],
  },
  {
    id: 'openai',
    icon: '☁',
    title: 'OpenAI',
    lines: ['Best AI quality. Requires an API key.', 'Small cost per session.'],
  },
  {
    id: 'fake',
    icon: '🎲',
    title: 'Try first, configure later',
    lines: ['Uses placeholder AI responses.', 'Good for exploring the app.'],
  },
]

function select(id: Exclude<SelectedProvider, null>) {
  // Clicking the already-selected card proceeds; otherwise just select it.
  if (selected.value === id) {
    emit('choose', id)
    return
  }
  selected.value = id
  emit('update:modelValue', id)
}

function proceed() {
  if (selected.value) emit('choose', selected.value)
}
</script>

<template>
  <div class="space-y-5">
    <h2 class="text-xl font-semibold text-white">How do you want to power the AI DM?</h2>

    <div class="space-y-3" role="radiogroup" aria-label="AI provider">
      <button
        v-for="opt in options"
        :key="opt.id"
        type="button"
        role="radio"
        :aria-checked="selected === opt.id"
        :data-provider="opt.id"
        :class="[
          'w-full rounded-lg border p-4 text-left transition',
          selected === opt.id
            ? 'border-aircane-500 bg-aircane-950/40 ring-1 ring-aircane-500'
            : 'border-gray-700 bg-gray-900 hover:border-gray-600',
        ]"
        @click="select(opt.id)"
      >
        <div class="flex items-center gap-2">
          <span class="text-lg" aria-hidden="true">{{ opt.icon }}</span>
          <span class="font-medium text-white">{{ opt.title }}</span>
          <span
            v-if="opt.recommended"
            class="ml-auto rounded-full bg-aircane-600/30 px-2 py-0.5 text-xs font-medium text-aircane-300"
          >
            ★ Recommended
          </span>
        </div>
        <div class="mt-2 space-y-0.5 text-sm text-gray-400">
          <p v-for="line in opt.lines" :key="line">{{ line }}</p>
        </div>
      </button>
    </div>

    <div class="flex items-center justify-between pt-2">
      <button
        type="button"
        class="text-sm text-gray-400 hover:text-gray-200"
        @click="$emit('back')"
      >
        ← Back
      </button>
      <button
        type="button"
        :disabled="!selected"
        class="rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
        @click="proceed"
      >
        Next →
      </button>
    </div>
  </div>
</template>

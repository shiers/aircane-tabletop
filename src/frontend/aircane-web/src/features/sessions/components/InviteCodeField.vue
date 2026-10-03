<script setup lang="ts">
/**
 * InviteCodeField - styled v-model text field for a session invite code.
 * A 🗝 key icon sits in the leading slot, a ✦ star ornament trails the input,
 * and the code is entered uppercased with wide letter-spacing (maxlength 8).
 */
const props = withDefaults(
  defineProps<{
    /** Current invite code value. */
    modelValue: string
    /** Inline validation error, rendered beneath the field with the friendly ornament. */
    error?: string
    /** Optional id applied to the input (lets callers keep a stable id for labels/tests). */
    id?: string
  }>(),
  {
    error: undefined,
    id: undefined,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
}>()

function onInput(event: Event): void {
  emit('update:modelValue', (event.target as HTMLInputElement).value)
}
</script>

<template>
  <div>
    <div class="relative flex items-center">
      <span class="pointer-events-none absolute left-3 text-lg" aria-hidden="true">🗝</span>
      <input
        :id="id"
        :value="modelValue"
        type="text"
        autocomplete="off"
        autocorrect="off"
        autocapitalize="characters"
        spellcheck="false"
        maxlength="8"
        placeholder="Enter invite code..."
        class="w-full rounded-md border bg-surface-850 py-2 pl-10 pr-10 text-center font-mono text-lg uppercase tracking-[0.4em] text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        :class="error ? 'border-red-600' : 'border-gray-700'"
        :aria-describedby="error && id ? `${id}-error` : undefined"
        :aria-invalid="!!error"
        @input="onInput"
      />
      <span class="pointer-events-none absolute right-3 text-lg text-aircane-400" aria-hidden="true">✦</span>
    </div>
    <p
      v-if="error"
      :id="id ? `${id}-error` : undefined"
      role="alert"
      class="mt-1 flex items-center gap-2 text-xs text-red-400"
    >
      <img
        src="/assets/join-session/friendly-error-ornament.png"
        alt=""
        aria-hidden="true"
        class="h-8 w-8 shrink-0 object-contain"
        @error="(e) => ((e.target as HTMLElement).style.display = 'none')"
      />
      <span>{{ error }}</span>
    </p>
  </div>
</template>

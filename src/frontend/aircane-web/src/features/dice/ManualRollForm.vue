<script setup lang="ts">
import { reactive, ref } from 'vue'
import { recordManualRoll, RollVisibility, type RollDto } from './api'

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** The session to record the roll in. */
  sessionId: string
  /** The participant submitting the roll. */
  rollerParticipantId: string
  /** Optional character to associate with the roll. */
  characterId?: string | null
}>()

const emit = defineEmits<{
  (e: 'recorded', roll: RollDto): void
}>()

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

const form = reactive({
  total: '' as string | number,
  formula: '',
  context: '',
  visibility: RollVisibility.Public,
})

const submitting = ref(false)
const error = ref<string | null>(null)
const result = ref<RollDto | null>(null)

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

async function handleSubmit(): Promise<void> {
  error.value = null
  result.value = null

  const total = Number(form.total)
  if (!form.total || isNaN(total)) {
    error.value = 'Total is required and must be a number.'
    return
  }

  submitting.value = true
  try {
    const roll = await recordManualRoll(props.sessionId, {
      rollerParticipantId: props.rollerParticipantId,
      dieResults: [total],
      modifier: 0,
      total,
      formula: form.formula.trim() || null,
      visibility: form.visibility,
      characterId: props.characterId ?? null,
      context: form.context.trim() || null,
    })

    result.value = roll
    emit('recorded', roll)

    // Reset form after successful submission
    form.total = ''
    form.formula = ''
    form.context = ''
    form.visibility = RollVisibility.Public
  } catch (err: unknown) {
    error.value =
      err instanceof Error ? err.message : 'Failed to record roll. Please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="space-y-4">
    <form novalidate class="space-y-4" @submit.prevent="handleSubmit">
      <!-- Total (required) -->
      <div>
        <label for="manual-roll-total" class="mb-1 block text-sm font-medium text-gray-300">
          Total <span aria-hidden="true" class="text-red-400">*</span>
        </label>
        <input
          id="manual-roll-total"
          v-model.number="form.total"
          type="number"
          required
          aria-required="true"
          placeholder="e.g. 17"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Formula (optional) -->
      <div>
        <label for="manual-roll-formula" class="mb-1 block text-sm font-medium text-gray-300">
          Formula
          <span class="ml-1 text-xs text-gray-500">(optional, e.g. 1d20+5)</span>
        </label>
        <input
          id="manual-roll-formula"
          v-model="form.formula"
          type="text"
          placeholder="e.g. 1d20+5"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Context (optional) -->
      <div>
        <label for="manual-roll-context" class="mb-1 block text-sm font-medium text-gray-300">
          Context
          <span class="ml-1 text-xs text-gray-500">(optional, e.g. Attack roll vs goblin)</span>
        </label>
        <input
          id="manual-roll-context"
          v-model="form.context"
          type="text"
          placeholder="e.g. Attack roll vs goblin"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Visibility toggle -->
      <div>
        <span class="mb-1 block text-sm font-medium text-gray-300">Visibility</span>
        <div class="flex gap-2" role="group" aria-label="Roll visibility">
          <button
            type="button"
            :class="[
              'rounded-lg px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
              form.visibility === RollVisibility.Public
                ? 'bg-aircane-600 text-white'
                : 'border border-gray-700 bg-gray-800 text-gray-400 hover:text-gray-200',
            ]"
            :aria-pressed="form.visibility === RollVisibility.Public"
            @click="form.visibility = RollVisibility.Public"
          >
            Public
          </button>
          <button
            type="button"
            :class="[
              'rounded-lg px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400',
              form.visibility === RollVisibility.Private
                ? 'bg-aircane-600 text-white'
                : 'border border-gray-700 bg-gray-800 text-gray-400 hover:text-gray-200',
            ]"
            :aria-pressed="form.visibility === RollVisibility.Private"
            @click="form.visibility = RollVisibility.Private"
          >
            Private
          </button>
        </div>
      </div>

      <!-- Error -->
      <p v-if="error" role="alert" class="text-sm text-red-400">{{ error }}</p>

      <!-- Submit -->
      <button
        type="submit"
        :disabled="submitting"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <span v-if="submitting" aria-hidden="true">Submitting…</span>
        <span v-else>Submit Manual Roll</span>
      </button>
    </form>

    <!-- Result display -->
    <div
      v-if="result"
      role="status"
      aria-live="polite"
      class="rounded-lg border border-gray-700 bg-gray-800 p-4 space-y-1"
    >
      <p class="text-sm font-medium text-gray-300">Roll recorded</p>
      <p class="text-2xl font-bold text-aircane-400">{{ result.total }}</p>
      <p v-if="result.formula" class="text-xs text-gray-500">Formula: {{ result.formula }}</p>
      <p v-if="result.context" class="text-xs text-gray-500">{{ result.context }}</p>
      <p class="text-xs text-gray-600">
        {{ result.visibility === RollVisibility.Private ? 'Private roll' : 'Public roll' }}
      </p>
    </div>
  </div>
</template>

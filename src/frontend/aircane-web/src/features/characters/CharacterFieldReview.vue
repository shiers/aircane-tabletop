<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  applyFieldMappings,
  CANONICAL_FIELD_OPTIONS,
  type UnmappedFieldDto,
  type FieldMappingEntry,
} from './api'

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** The ID of the draft character that needs field review. */
  characterId: string
  /** Unmapped fields returned by the PDF import endpoint. */
  unmappedFields: UnmappedFieldDto[]
  /** Non-fatal warnings from the extraction step. */
  warnings?: string[]
}>()

// ---------------------------------------------------------------------------
// Router
// ---------------------------------------------------------------------------

const router = useRouter()

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

/** One editable row per unmapped field. */
interface ReviewRow {
  sourceFieldName: string
  sourceValue: string
  /** The canonical field path the user has selected (or the suggestion). */
  selectedCanonicalField: string
  /** The value the user wants to write to the canonical field. */
  mappedValue: string
  /** Whether this row should be included in the save payload. */
  include: boolean
  /** Confidence of the system suggestion (0–1). */
  confidence: number
}

const rows = ref<ReviewRow[]>([])
const saving = ref(false)
const error = ref<string | null>(null)
const successMessage = ref<string | null>(null)

// ---------------------------------------------------------------------------
// Initialise rows from props
// ---------------------------------------------------------------------------

onMounted(() => {
  rows.value = props.unmappedFields.map((field) => ({
    sourceFieldName: field.sourceFieldName,
    sourceValue: field.sourceValue,
    selectedCanonicalField: field.suggestedCanonicalField ?? '',
    mappedValue: field.sourceValue,
    include: field.suggestedCanonicalField !== null,
    confidence: field.confidence,
  }))
})

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

const includedRows = computed(() => rows.value.filter((r) => r.include))

const hasIncludedRows = computed(() => includedRows.value.length > 0)

/** Returns a human-readable label for a confidence score. */
function confidenceLabel(confidence: number): string {
  if (confidence >= 0.8) return 'High'
  if (confidence >= 0.5) return 'Medium'
  if (confidence > 0) return 'Low'
  return 'None'
}

function confidenceClass(confidence: number): string {
  if (confidence >= 0.8) return 'text-green-400'
  if (confidence >= 0.5) return 'text-yellow-400'
  if (confidence > 0) return 'text-orange-400'
  return 'text-gray-500'
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

/** Save the selected mappings and navigate to the character detail page. */
async function saveMappings(): Promise<void> {
  error.value = null
  saving.value = true

  try {
    const mappings: FieldMappingEntry[] = includedRows.value
      .filter((r) => r.selectedCanonicalField.trim() !== '')
      .map((r) => ({
        sourceFieldName: r.sourceFieldName,
        canonicalFieldPath: r.selectedCanonicalField.trim(),
        value: r.mappedValue,
      }))

    await applyFieldMappings(props.characterId, { mappings })

    successMessage.value = 'Mappings saved successfully.'
    await router.push({ name: 'characters' })
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'An unexpected error occurred.'
  } finally {
    saving.value = false
  }
}

/** Skip the review and save the character as-is (no additional mappings applied). */
async function skipReview(): Promise<void> {
  error.value = null
  saving.value = true

  try {
    // Apply an empty mappings list - the character is already persisted as a draft.
    await applyFieldMappings(props.characterId, { mappings: [] })
    await router.push({ name: 'characters' })
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'An unexpected error occurred.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <main class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Page header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-5xl items-center justify-between">
        <div class="flex items-center gap-3">
          <RouterLink
            to="/characters"
            class="text-sm text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-aircane-400"
            aria-label="Back to characters"
          >
            ← Characters
          </RouterLink>
          <span class="text-gray-700" aria-hidden="true">/</span>
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">Review Imported Fields</h1>
        </div>
      </div>
    </header>

    <div class="mx-auto max-w-5xl space-y-6 px-6 py-8">

      <!-- Intro banner -->
      <div class="rounded-lg border border-yellow-800 bg-yellow-950/40 px-4 py-3 text-sm text-yellow-300">
        <p class="font-semibold">Some fields could not be automatically mapped.</p>
        <p class="mt-1 text-yellow-400">
          Review the fields below, select the correct canonical target for each one, and adjust the
          value if needed. Uncheck any row you want to skip. Click
          <strong>Save Mappings</strong> when done, or <strong>Skip</strong> to save the character
          as-is without applying any additional mappings.
        </p>
      </div>

      <!-- Extraction warnings -->
      <div
        v-if="warnings && warnings.length > 0"
        class="rounded-lg border border-gray-700 bg-gray-900 px-4 py-3"
      >
        <p class="mb-1 text-sm font-semibold text-gray-300">Extraction warnings:</p>
        <ul class="list-inside list-disc space-y-0.5 text-sm text-gray-400">
          <li v-for="(w, i) in warnings" :key="i">{{ w }}</li>
        </ul>
      </div>

      <!-- Error banner -->
      <div
        v-if="error"
        role="alert"
        class="flex items-start gap-3 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
      >
        <svg
          class="mt-0.5 h-4 w-4 shrink-0 text-red-400"
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 20 20"
          fill="currentColor"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M10 18a8 8 0 100-16 8 8 0 000 16zm-.75-9.25a.75.75 0 011.5 0v3.5a.75.75 0 01-1.5 0v-3.5zm.75 6a.75.75 0 100-1.5.75.75 0 000 1.5z"
            clip-rule="evenodd"
          />
        </svg>
        <span>{{ error }}</span>
      </div>

      <!-- Success message -->
      <output
        v-if="successMessage"
        class="block rounded-lg border border-green-800 bg-green-950 px-4 py-3 text-sm text-green-300"
      >
        {{ successMessage }}
      </output>

      <!-- Field mapping table -->
      <section aria-labelledby="field-review-heading">
        <h2 id="field-review-heading" class="sr-only">Unmapped fields</h2>

        <div
          v-if="rows.length === 0"
          class="rounded-lg border border-gray-800 bg-gray-900 px-6 py-8 text-center text-gray-400"
        >
          No unmapped fields to review.
        </div>

        <div v-else class="overflow-x-auto rounded-xl border border-gray-800">
          <table class="w-full text-sm">
            <thead class="bg-gray-900 text-left text-xs font-semibold uppercase tracking-wider text-gray-400">
              <tr>
                <th scope="col" class="w-10 px-4 py-3">
                  <span class="sr-only">Include</span>
                </th>
                <th scope="col" class="px-4 py-3">Source Field</th>
                <th scope="col" class="px-4 py-3">Source Value</th>
                <th scope="col" class="px-4 py-3">Map To</th>
                <th scope="col" class="px-4 py-3">Value to Save</th>
                <th scope="col" class="px-4 py-3">Confidence</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-800 bg-gray-950">
              <tr
                v-for="(row, idx) in rows"
                :key="row.sourceFieldName"
                :class="[
                  'transition-colors',
                  row.include ? 'bg-gray-950' : 'opacity-50',
                ]"
              >
                <!-- Include checkbox -->
                <td class="px-4 py-3">
                  <input
                    :id="`include-${idx}`"
                    v-model="row.include"
                    type="checkbox"
                    class="h-4 w-4 rounded border-gray-600 bg-gray-800 text-aircane-500 focus:ring-2 focus:ring-aircane-400"
                    :aria-label="`Include field ${row.sourceFieldName}`"
                  />
                </td>

                <!-- Source field name -->
                <td class="px-4 py-3">
                  <code class="rounded bg-gray-800 px-1.5 py-0.5 text-xs text-gray-300">
                    {{ row.sourceFieldName }}
                  </code>
                </td>

                <!-- Source value -->
                <td class="px-4 py-3 text-gray-300">
                  {{ row.sourceValue || '-' }}
                </td>

                <!-- Canonical field dropdown -->
                <td class="px-4 py-3">
                  <label :for="`canonical-${idx}`" class="sr-only">
                    Canonical field for {{ row.sourceFieldName }}
                  </label>
                  <select
                    :id="`canonical-${idx}`"
                    v-model="row.selectedCanonicalField"
                    :disabled="!row.include"
                    class="block w-full min-w-[180px] rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    <option value="">- Skip this field -</option>
                    <option
                      v-for="opt in CANONICAL_FIELD_OPTIONS"
                      :key="opt.value"
                      :value="opt.value"
                    >
                      {{ opt.label }}
                    </option>
                  </select>
                </td>

                <!-- Value to save -->
                <td class="px-4 py-3">
                  <label :for="`value-${idx}`" class="sr-only">
                    Value for {{ row.sourceFieldName }}
                  </label>
                  <input
                    :id="`value-${idx}`"
                    v-model="row.mappedValue"
                    type="text"
                    :disabled="!row.include || !row.selectedCanonicalField"
                    class="block w-full min-w-[120px] rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:cursor-not-allowed disabled:opacity-50"
                    :placeholder="row.sourceValue"
                  />
                </td>

                <!-- Confidence -->
                <td class="px-4 py-3">
                  <span
                    :class="['text-xs font-medium', confidenceClass(row.confidence)]"
                    :title="`Confidence: ${Math.round(row.confidence * 100)}%`"
                  >
                    {{ confidenceLabel(row.confidence) }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Action buttons -->
      <div class="flex items-center justify-between border-t border-gray-800 pt-4">
        <button
          type="button"
          :disabled="saving"
          class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500 disabled:cursor-not-allowed disabled:opacity-50"
          @click="skipReview"
        >
          Skip - save as-is
        </button>

        <button
          type="button"
          :disabled="saving || !hasIncludedRows"
          class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
          @click="saveMappings"
        >
          <svg
            v-if="saving"
            class="h-4 w-4 animate-spin"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
          </svg>
          {{ saving ? 'Saving…' : 'Save Mappings' }}
        </button>
      </div>

    </div>
  </main>
</template>

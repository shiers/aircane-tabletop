<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  applyFieldMappings,
  type SourceImportResponse,
  type FieldMappingEntry,
  type UnmappedFieldDto,
} from '../api'
import { previewCharacterForm } from '@/features/game-systems/api'
import type { FormDescriptor } from '@/features/game-systems/types'
import CharacterSchemaRenderer from '@/features/game-systems/components/CharacterSchemaRenderer.vue'
import { sourceBadge } from './sourceBadges'

// ---------------------------------------------------------------------------
// Props / emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** The whole import response envelope (source/ruleset from root, form data from .review). */
  response: SourceImportResponse
}>()

const emit = defineEmits<{
  /** Emitted after the confirmed mappings are applied to the draft character. */
  (e: 'confirmed'): void
  /** Emitted when the user cancels the review. */
  (e: 'cancel'): void
}>()

// ---------------------------------------------------------------------------
// Schema-field-id ↔ ApplyMapping-path bridge table (local to this panel).
// The FormDescriptor renders schema field ids (str, hp_max); the save flow and
// response.review.mappedFields speak ApplyMapping paths (abilities.strength,
// combat.maxHitPoints). This table is the single documented bridge between the two.
// Only paths that have a corresponding schema field id are listed; experiencePoints,
// passivePerception, initiative, proficiencyBonus have no schema field id.
// ---------------------------------------------------------------------------

const SCHEMA_ID_TO_PATH: Record<string, string> = {
  name: 'identity.name',
  str: 'abilities.strength',
  dex: 'abilities.dexterity',
  con: 'abilities.constitution',
  int: 'abilities.intelligence',
  wis: 'abilities.wisdom',
  cha: 'abilities.charisma',
  ac: 'combat.armorClass',
  hp_max: 'combat.maxHitPoints',
  hp_current: 'combat.hitPoints',
  class: 'class',
  level: 'level',
  race: 'identity.raceOrAncestry',
  background: 'identity.background',
}

/** Reverse lookup: ApplyMapping path → schema field id. */
const PATH_TO_SCHEMA_ID: Record<string, string> = Object.fromEntries(
  Object.entries(SCHEMA_ID_TO_PATH).map(([id, path]) => [path, id]),
)

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const descriptor = ref<FormDescriptor>({ sections: [] })
/** The schema-field-id-keyed model consumed by CharacterSchemaRenderer. */
const formModel = ref<Record<string, unknown>>({})
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)
/** The ruleset value shown in the editable 2014/2024 dropdown (D&D 5e only). */
const selectedRuleset = ref<string>(props.response.ruleset ?? '')

// ---------------------------------------------------------------------------
// Computed — bind envelope signals from response.*; form data from response.review.*
// ---------------------------------------------------------------------------

const review = computed(() => props.response.review)
const badge = computed(() => sourceBadge(props.response.detectedSource))

/** mappedFields are ApplyMapping paths; keep them as the source of truth for values. */
const mappedFields = computed<Record<string, string>>(() => review.value.mappedFields ?? {})

/** Fields that have no ApplyMapping schema id — rendered read-only, never submitted. */
const unmappedFields = computed<UnmappedFieldDto[]>(() => review.value.unmappedFields ?? [])

/** True when the detected ruleset should be confirmed (amber highlight + tooltip). */
const rulesetNeedsConfirm = computed(() => props.response.rulesetRequiresConfirmation)

/** Whether to render the D&D 5e ruleset dropdown (2014/2024 choices). */
const showRulesetDropdown = computed(
  () => props.response.ruleset === '2014' || props.response.ruleset === '2024',
)

// ---------------------------------------------------------------------------
// Load the FormDescriptor and seed the model from mappedFields via the bridge table.
// ---------------------------------------------------------------------------

onMounted(async () => {
  const systemId = review.value.gameSystemDefinitionId
  try {
    if (systemId) {
      descriptor.value = await previewCharacterForm(systemId)
    }
    seedModel()
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to load the character form.'
  } finally {
    loading.value = false
  }
})

/** Seed the schema-id-keyed form model from the ApplyMapping-path mappedFields. */
function seedModel(): void {
  const model: Record<string, unknown> = {}
  for (const [path, value] of Object.entries(mappedFields.value)) {
    const schemaId = PATH_TO_SCHEMA_ID[path]
    if (schemaId) model[schemaId] = value
  }
  formModel.value = model
}

/** True when the given ApplyMapping path is flagged for review. */
function pathRequiresReview(path: string): boolean {
  return unmappedFields.value.some(
    (f) => f.requiresReview && f.suggestedCanonicalField === path,
  )
}

const reviewHighlightPaths = computed(() =>
  Object.keys(mappedFields.value).filter(pathRequiresReview),
)

// ---------------------------------------------------------------------------
// Confirm — synthesize FieldMappingEntry[] from the (edited) model and submit.
// ---------------------------------------------------------------------------

async function confirm(): Promise<void> {
  error.value = null
  saving.value = true
  try {
    const mappings: FieldMappingEntry[] = []
    for (const [schemaId, path] of Object.entries(SCHEMA_ID_TO_PATH)) {
      // Only submit fields the import actually mapped (present in mappedFields).
      if (!(path in mappedFields.value)) continue
      const edited = formModel.value[schemaId]
      const value = edited === undefined || edited === null ? mappedFields.value[path] : String(edited)
      mappings.push({
        // MEDIUM-2: sourceFieldName must be a non-null label so the entry is well-formed.
        sourceFieldName: path,
        canonicalFieldPath: path,
        value,
      })
    }

    await applyFieldMappings(review.value.characterId, { mappings })
    emit('confirmed')
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to save the imported character.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="space-y-5">
    <!-- Read-only "Imported from" header, bound from response.detectedSource -->
    <div class="flex items-center gap-2 text-sm text-gray-300">
      <span>Imported from:</span>
      <span
        data-testid="imported-from-badge"
        :class="[
          'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold',
          badge.classes,
        ]"
      >
        {{ badge.label }}
      </span>
    </div>

    <!-- D&D 5e ruleset dropdown (editable 2014/2024), amber when confirmation required -->
    <div v-if="showRulesetDropdown">
      <label for="review-ruleset" class="mb-1 block text-xs font-medium text-gray-300">
        Ruleset
      </label>
      <select
        id="review-ruleset"
        v-model="selectedRuleset"
        :title="rulesetNeedsConfirm ? 'Please confirm the detected ruleset before saving.' : undefined"
        :class="[
          'block w-full rounded-lg border bg-gray-800 px-3 py-1.5 text-sm text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-500',
          rulesetNeedsConfirm
            ? 'border-amber-500 ring-1 ring-amber-500/50'
            : 'border-gray-700 focus:border-aircane-500',
        ]"
      >
        <option value="2014">2014</option>
        <option value="2024">2024</option>
      </select>
      <p v-if="rulesetNeedsConfirm" class="mt-1 text-xs text-amber-400">
        We guessed this ruleset. Confirm it before saving.
      </p>
    </div>

    <!-- Loading state -->
    <div v-if="loading" class="py-6 text-center text-sm text-gray-400">Loading character form…</div>

    <!-- Error banner -->
    <div
      v-if="error"
      role="alert"
      class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
    >
      {{ error }}
    </div>

    <!-- Dynamic character sheet (FormDescriptor-driven), seeded from mappedFields -->
    <CharacterSchemaRenderer
      v-if="!loading"
      v-model="formModel"
      :descriptor="descriptor"
    />

    <!-- Fields flagged for review -->
    <div
      v-if="reviewHighlightPaths.length > 0"
      class="rounded-lg border border-amber-800 bg-amber-950/40 px-4 py-3 text-sm text-amber-300"
    >
      <p class="font-semibold">Please double-check these imported fields:</p>
      <ul class="mt-1 list-inside list-disc space-y-0.5">
        <li v-for="path in reviewHighlightPaths" :key="path">{{ path }}</li>
      </ul>
    </div>

    <!-- Read-only unmapped fields (no ApplyMapping path) — displayed, never submitted -->
    <div
      v-if="unmappedFields.length > 0"
      class="rounded-lg border border-surface-700/50 bg-surface-850 px-4 py-3"
    >
      <p class="mb-2 text-xs font-semibold uppercase tracking-wider text-gray-400">
        Extra fields (not imported)
      </p>
      <dl class="space-y-1 text-sm">
        <div v-for="(f, i) in unmappedFields" :key="`${f.sourceFieldName}-${i}`" class="flex gap-2">
          <dt class="font-medium text-gray-300">{{ f.sourceFieldName }}:</dt>
          <dd class="text-gray-400">{{ f.sourceValue || '—' }}</dd>
        </div>
      </dl>
    </div>

    <!-- Actions -->
    <div class="flex items-center justify-end gap-3 border-t border-gray-800 pt-4">
      <button
        type="button"
        :disabled="saving"
        class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500 disabled:cursor-not-allowed disabled:opacity-50"
        @click="emit('cancel')"
      >
        Cancel
      </button>
      <button
        type="button"
        :disabled="saving || loading"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
        @click="confirm"
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
        {{ saving ? 'Saving…' : 'Confirm & Save' }}
      </button>
    </div>
  </div>
</template>

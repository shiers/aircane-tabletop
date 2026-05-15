<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import {
  listTemplates,
  saveTemplate,
  type CharacterTemplateDto,
  type SaveTemplateRequest,
  type TemplateFieldDefinition,
  CANONICAL_FIELD_OPTIONS,
} from './api'

// ---------------------------------------------------------------------------
// Props / emits
// ---------------------------------------------------------------------------

const emit = defineEmits<{
  /** Emitted when the user clicks "Use Template" - passes the template's field definitions
   *  so the parent can pre-fill the character creation form. */
  useTemplate: [template: CharacterTemplateDto]
}>()

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const templates = ref<CharacterTemplateDto[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

// Save-as-template form state
const showSaveForm = ref(false)
const saving = ref(false)
const saveError = ref<string | null>(null)
const saveSuccess = ref<string | null>(null)

const newTemplateName = ref('')
const newTemplateGameSystem = ref('D&D 5e')
const newTemplateRuleset = ref('2014')

/** Field rows for the save form - each row maps a canonical field to a display group. */
interface FieldRow {
  fieldName: string
  canonicalFieldPath: string
  dataType: 'string' | 'int' | 'bool'
  displayGroup: string
  isRequired: boolean
  defaultValue: string
  sourceCoordinate: string
}

const fieldRows = ref<FieldRow[]>([])

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

onMounted(async () => {
  await fetchTemplates()
})

// ---------------------------------------------------------------------------
// Computed
// ---------------------------------------------------------------------------

const hasTemplates = computed(() => templates.value.length > 0)

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

// ---------------------------------------------------------------------------
// Actions
// ---------------------------------------------------------------------------

async function fetchTemplates(): Promise<void> {
  loading.value = true
  error.value = null
  try {
    templates.value = await listTemplates()
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to load templates.'
  } finally {
    loading.value = false
  }
}

function openSaveForm(): void {
  newTemplateName.value = ''
  newTemplateGameSystem.value = 'D&D 5e'
  newTemplateRuleset.value = '2014'
  fieldRows.value = [buildEmptyFieldRow()]
  saveError.value = null
  saveSuccess.value = null
  showSaveForm.value = true
}

function closeSaveForm(): void {
  showSaveForm.value = false
}

function buildEmptyFieldRow(): FieldRow {
  return {
    fieldName: '',
    canonicalFieldPath: '',
    dataType: 'string',
    displayGroup: '',
    isRequired: false,
    defaultValue: '',
    sourceCoordinate: '',
  }
}

function addFieldRow(): void {
  fieldRows.value.push(buildEmptyFieldRow())
}

function removeFieldRow(index: number): void {
  fieldRows.value.splice(index, 1)
}

/** When the user picks a canonical field path, auto-fill the field name and data type. */
function onCanonicalPathChange(row: FieldRow): void {
  const opt = CANONICAL_FIELD_OPTIONS.find((o) => o.value === row.canonicalFieldPath)
  if (opt && !row.fieldName) {
    row.fieldName = opt.label
  }
  // Infer data type from path
  const intPaths = [
    'level', 'abilities.strength', 'abilities.dexterity', 'abilities.constitution',
    'abilities.intelligence', 'abilities.wisdom', 'abilities.charisma',
    'combat.armorClass', 'combat.hitPoints', 'combat.maxHitPoints',
    'combat.speed', 'combat.initiative', 'combat.proficiencyBonus', 'passivePerception',
  ]
  if (intPaths.includes(row.canonicalFieldPath)) {
    row.dataType = 'int'
  } else {
    row.dataType = 'string'
  }
}

async function handleSaveTemplate(): Promise<void> {
  saveError.value = null
  saveSuccess.value = null

  if (!newTemplateName.value.trim()) {
    saveError.value = 'Template name is required.'
    return
  }
  if (!newTemplateGameSystem.value.trim()) {
    saveError.value = 'Game system is required.'
    return
  }
  if (!newTemplateRuleset.value.trim()) {
    saveError.value = 'Ruleset is required.'
    return
  }

  const fieldDefinitions: TemplateFieldDefinition[] = fieldRows.value
    .filter((r) => r.canonicalFieldPath.trim() !== '')
    .map((r) => ({
      fieldName: r.fieldName.trim() || r.canonicalFieldPath,
      canonicalFieldPath: r.canonicalFieldPath.trim(),
      dataType: r.dataType,
      displayGroup: r.displayGroup.trim() || 'General',
      isRequired: r.isRequired,
      defaultValue: r.defaultValue.trim() || null,
      sourceCoordinate: r.sourceCoordinate.trim() || null,
    }))

  const request: SaveTemplateRequest = {
    name: newTemplateName.value.trim(),
    gameSystem: newTemplateGameSystem.value.trim(),
    ruleset: newTemplateRuleset.value.trim(),
    fieldDefinitions,
  }

  saving.value = true
  try {
    const saved = await saveTemplate(request)
    templates.value.unshift(saved)
    saveSuccess.value = `Template "${saved.name}" saved.`
    showSaveForm.value = false
  } catch (err: unknown) {
    saveError.value = err instanceof Error ? err.message : 'Failed to save template.'
  } finally {
    saving.value = false
  }
}

function handleUseTemplate(template: CharacterTemplateDto): void {
  emit('useTemplate', template)
}
</script>

<template>
  <section aria-labelledby="templates-heading" class="space-y-6">
    <!-- Section header -->
    <div class="flex items-center justify-between">
      <h2 id="templates-heading" class="text-lg font-semibold text-white">
        Character Templates
      </h2>
      <button
        type="button"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="openSaveForm"
      >
        <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
          <path
            d="M10.75 4.75a.75.75 0 00-1.5 0v4.5h-4.5a.75.75 0 000 1.5h4.5v4.5a.75.75 0 001.5 0v-4.5h4.5a.75.75 0 000-1.5h-4.5v-4.5z"
          />
        </svg>
        Save as Template
      </button>
    </div>

    <!-- Success banner -->
    <output
      v-if="saveSuccess"
      class="block rounded-lg border border-green-800 bg-green-950 px-4 py-3 text-sm text-green-300"
    >
      {{ saveSuccess }}
    </output>

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

    <!-- Loading state -->
    <div
      v-if="loading"
      class="flex items-center justify-center py-12 text-gray-400"
      aria-live="polite"
      aria-busy="true"
    >
      <svg class="mr-2 h-5 w-5 animate-spin" viewBox="0 0 24 24" fill="none" aria-hidden="true">
        <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
        <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
      </svg>
      Loading templates…
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!hasTemplates"
      class="rounded-xl border border-dashed border-gray-700 bg-gray-900 px-6 py-12 text-center"
    >
      <svg
        class="mx-auto mb-3 h-10 w-10 text-gray-600"
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        aria-hidden="true"
      >
        <path
          stroke-linecap="round"
          stroke-linejoin="round"
          stroke-width="1.5"
          d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
        />
      </svg>
      <p class="text-sm font-medium text-gray-400">No templates saved yet.</p>
      <p class="mt-1 text-xs text-gray-500">
        Import a character sheet and save its field mapping as a template to reuse it for future
        characters.
      </p>
    </div>

    <!-- Template list -->
    <ul v-else class="space-y-3" aria-label="Saved character templates">
      <li
        v-for="template in templates"
        :key="template.id"
        class="flex items-center justify-between gap-4 rounded-xl border border-gray-800 bg-gray-900 px-5 py-4"
      >
        <div class="min-w-0 flex-1">
          <p class="truncate text-sm font-semibold text-white">{{ template.name }}</p>
          <p class="mt-0.5 text-xs text-gray-400">
            {{ template.gameSystem }} · {{ template.ruleset }} ·
            {{ template.fieldDefinitions.length }} field{{ template.fieldDefinitions.length === 1 ? '' : 's' }}
          </p>
          <p class="mt-0.5 text-xs text-gray-500">Saved {{ formatDate(template.createdAt) }}</p>
        </div>

        <button
          type="button"
          class="shrink-0 rounded-lg border border-gray-600 bg-gray-800 px-3 py-1.5 text-xs font-semibold text-gray-300 hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          :aria-label="`Use template ${template.name}`"
          @click="handleUseTemplate(template)"
        >
          Use Template
        </button>
      </li>
    </ul>

    <!-- Save-as-template form modal -->
    <Teleport to="body">
      <div
        v-if="showSaveForm"
        class="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/70 px-4 py-12"
        aria-modal="true"
        aria-labelledby="save-template-heading"
      >
        <div class="w-full max-w-2xl rounded-2xl border border-gray-700 bg-gray-900 shadow-2xl">
          <!-- Modal header -->
          <div class="flex items-center justify-between border-b border-gray-800 px-6 py-4">
            <h3 id="save-template-heading" class="text-base font-semibold text-white">
              Save as Template
            </h3>
            <button
              type="button"
              class="rounded-lg p-1 text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
              aria-label="Close save template form"
              @click="closeSaveForm"
            >
              <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path
                  d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z"
                />
              </svg>
            </button>
          </div>

          <!-- Modal body -->
          <div class="space-y-5 px-6 py-5">
            <!-- Save error -->
            <div
              v-if="saveError"
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
              <span>{{ saveError }}</span>
            </div>

            <!-- Template metadata -->
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <div class="sm:col-span-3">
                <label for="template-name" class="mb-1 block text-xs font-medium text-gray-300">
                  Template Name <span class="text-red-400" aria-hidden="true">*</span>
                </label>
                <input
                  id="template-name"
                  v-model="newTemplateName"
                  type="text"
                  required
                  placeholder="e.g. D&D 5e Standard Sheet"
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                />
              </div>

              <div>
                <label for="template-game-system" class="mb-1 block text-xs font-medium text-gray-300">
                  Game System <span class="text-red-400" aria-hidden="true">*</span>
                </label>
                <input
                  id="template-game-system"
                  v-model="newTemplateGameSystem"
                  type="text"
                  required
                  placeholder="D&D 5e"
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                />
              </div>

              <div>
                <label for="template-ruleset" class="mb-1 block text-xs font-medium text-gray-300">
                  Ruleset <span class="text-red-400" aria-hidden="true">*</span>
                </label>
                <input
                  id="template-ruleset"
                  v-model="newTemplateRuleset"
                  type="text"
                  required
                  placeholder="2014"
                  class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                />
              </div>
            </div>

            <!-- Field definitions -->
            <div>
              <div class="mb-2 flex items-center justify-between">
                <p class="text-xs font-medium text-gray-300">Field Definitions</p>
                <button
                  type="button"
                  class="text-xs text-aircane-400 hover:text-aircane-300 focus:outline-none focus:underline"
                  @click="addFieldRow"
                >
                  + Add field
                </button>
              </div>

              <div
                v-if="fieldRows.length === 0"
                class="rounded-lg border border-dashed border-gray-700 px-4 py-6 text-center text-xs text-gray-500"
              >
                No fields added yet. Click "+ Add field" to define the template fields.
              </div>

              <div v-else class="space-y-2">
                <div
                  v-for="(row, idx) in fieldRows"
                  :key="idx"
                  class="grid grid-cols-12 gap-2 rounded-lg border border-gray-800 bg-gray-950 p-3"
                >
                  <!-- Canonical field path -->
                  <div class="col-span-4">
                    <label :for="`field-path-${idx}`" class="mb-1 block text-xs text-gray-400">
                      Canonical Field
                    </label>
                    <select
                      :id="`field-path-${idx}`"
                      v-model="row.canonicalFieldPath"
                      class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-2 py-1.5 text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                      @change="onCanonicalPathChange(row)"
                    >
                      <option value="">- Select -</option>
                      <option
                        v-for="opt in CANONICAL_FIELD_OPTIONS"
                        :key="opt.value"
                        :value="opt.value"
                      >
                        {{ opt.label }}
                      </option>
                    </select>
                  </div>

                  <!-- Field name -->
                  <div class="col-span-3">
                    <label :for="`field-name-${idx}`" class="mb-1 block text-xs text-gray-400">
                      Label
                    </label>
                    <input
                      :id="`field-name-${idx}`"
                      v-model="row.fieldName"
                      type="text"
                      placeholder="Display name"
                      class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-2 py-1.5 text-xs text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                    />
                  </div>

                  <!-- Display group -->
                  <div class="col-span-2">
                    <label :for="`field-group-${idx}`" class="mb-1 block text-xs text-gray-400">
                      Group
                    </label>
                    <input
                      :id="`field-group-${idx}`"
                      v-model="row.displayGroup"
                      type="text"
                      placeholder="e.g. Abilities"
                      class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-2 py-1.5 text-xs text-gray-100 placeholder-gray-600 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
                    />
                  </div>

                  <!-- Required toggle -->
                  <div class="col-span-2 flex flex-col items-center justify-center gap-1">
                    <label :for="`field-required-${idx}`" class="text-xs text-gray-400">
                      Required
                    </label>
                    <input
                      :id="`field-required-${idx}`"
                      v-model="row.isRequired"
                      type="checkbox"
                      class="h-4 w-4 rounded border-gray-600 bg-gray-800 text-aircane-500 focus:ring-2 focus:ring-aircane-400"
                    />
                  </div>

                  <!-- Remove row -->
                  <div class="col-span-1 flex items-end justify-center pb-1">
                    <button
                      type="button"
                      class="rounded p-1 text-gray-500 hover:text-red-400 focus:outline-none focus:ring-2 focus:ring-red-500"
                      :aria-label="`Remove field row ${idx + 1}`"
                      @click="removeFieldRow(idx)"
                    >
                      <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                        <path
                          fill-rule="evenodd"
                          d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.52.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z"
                          clip-rule="evenodd"
                        />
                      </svg>
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Modal footer -->
          <div class="flex items-center justify-end gap-3 border-t border-gray-800 px-6 py-4">
            <button
              type="button"
              :disabled="saving"
              class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500 disabled:cursor-not-allowed disabled:opacity-50"
              @click="closeSaveForm"
            >
              Cancel
            </button>
            <button
              type="button"
              :disabled="saving"
              class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
              @click="handleSaveTemplate"
            >
              <svg
                v-if="saving"
                class="h-4 w-4 animate-spin"
                viewBox="0 0 24 24"
                fill="none"
                aria-hidden="true"
              >
                <circle
                  class="opacity-25"
                  cx="12"
                  cy="12"
                  r="10"
                  stroke="currentColor"
                  stroke-width="4"
                />
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
              </svg>
              {{ saving ? 'Saving…' : 'Save Template' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </section>
</template>

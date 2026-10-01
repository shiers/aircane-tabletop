<script setup lang="ts">
import { reactive, ref, computed, onMounted } from 'vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'
import DiceConventionPreview from './DiceConventionPreview.vue'
import type { ValidationResult } from '../types'

const props = defineProps<{
  /** When provided, the editor operates in edit mode. */
  definitionId?: string
}>()

const emit = defineEmits<{
  (e: 'saved', id: string): void
  (e: 'cancel'): void
}>()

const store = useGameSystemStore()

// ── Tabs ─────────────────────────────────────────────────────────────────────

const tabs = [
  { id: 'metadata', label: 'Metadata' },
  { id: 'diceConventions', label: 'Dice Conventions' },
  { id: 'resolutionRules', label: 'Resolution Rules' },
  { id: 'characterSchema', label: 'Character Schema' },
  { id: 'conditionSet', label: 'Conditions' },
  { id: 'actionEconomy', label: 'Action Economy' },
  { id: 'encounterBudget', label: 'Encounter Budget' },
  { id: 'aiGuidance', label: 'AI Notes' },
] as const

const activeTab = ref<string>('metadata')

// ── Form state ───────────────────────────────────────────────────────────────

const form = reactive({
  metadata: {
    name: '',
    version: '1.0.0',
    identifier: '',
    publisher: '',
    genre: '',
    description: '',
    license: 'user-created',
  },
  diceConventions: '{}',
  resolutionRules: '{}',
  characterSchema: '{}',
  conditionSet: '{}',
  actionEconomy: '{}',
  encounterBudget: '{}',
  aiGuidance: '{}',
})

const saving = ref(false)
const validationResult = ref<ValidationResult | null>(null)
const localError = ref<string | null>(null)

// ── Computed ─────────────────────────────────────────────────────────────────

const isEditMode = computed(() => !!props.definitionId)

const definitionJson = computed(() => {
  try {
    const definition: Record<string, unknown> = {
      schemaVersion: 1,
      metadata: {
        ...form.metadata,
      },
      diceConventions: JSON.parse(form.diceConventions),
      resolutionRules: JSON.parse(form.resolutionRules),
      characterSchema: JSON.parse(form.characterSchema),
      conditionSet: JSON.parse(form.conditionSet),
      actionEconomy: JSON.parse(form.actionEconomy),
      encounterBudget: JSON.parse(form.encounterBudget),
      aiGuidance: JSON.parse(form.aiGuidance),
    }
    return JSON.stringify(definition, null, 2)
  } catch {
    return null
  }
})

// ── Lifecycle ────────────────────────────────────────────────────────────────

onMounted(async () => {
  await store.fetchTemplates()

  if (props.definitionId) {
    const detail = await store.fetchById(props.definitionId)
    if (detail?.definitionJson) {
      try {
        const parsed = JSON.parse(detail.definitionJson)
        form.metadata.name = parsed.metadata?.name ?? ''
        form.metadata.version = parsed.metadata?.version ?? '1.0.0'
        form.metadata.identifier = parsed.metadata?.id ?? parsed.metadata?.identifier ?? ''
        form.metadata.publisher = parsed.metadata?.publisher ?? ''
        form.metadata.genre = parsed.metadata?.genre ?? ''
        form.metadata.description = parsed.metadata?.description ?? ''
        form.metadata.license = parsed.metadata?.license ?? 'user-created'
        form.diceConventions = JSON.stringify(parsed.diceConventions ?? {}, null, 2)
        form.resolutionRules = JSON.stringify(parsed.resolutionRules ?? {}, null, 2)
        form.characterSchema = JSON.stringify(parsed.characterSchema ?? {}, null, 2)
        form.conditionSet = JSON.stringify(parsed.conditionSet ?? {}, null, 2)
        form.actionEconomy = JSON.stringify(parsed.actionEconomy ?? {}, null, 2)
        form.encounterBudget = JSON.stringify(parsed.encounterBudget ?? {}, null, 2)
        form.aiGuidance = JSON.stringify(parsed.aiGuidance ?? {}, null, 2)
      } catch {
        localError.value = 'Failed to parse existing definition.'
      }
    }
  }
})

// ── Actions ──────────────────────────────────────────────────────────────────

function applyTemplate(templateJson: string): void {
  try {
    const parsed = JSON.parse(templateJson)
    form.metadata.name = parsed.metadata?.name ?? form.metadata.name
    form.metadata.genre = parsed.metadata?.genre ?? form.metadata.genre
    form.diceConventions = JSON.stringify(parsed.diceConventions ?? {}, null, 2)
    form.resolutionRules = JSON.stringify(parsed.resolutionRules ?? {}, null, 2)
    form.characterSchema = JSON.stringify(parsed.characterSchema ?? {}, null, 2)
    form.conditionSet = JSON.stringify(parsed.conditionSet ?? {}, null, 2)
    form.actionEconomy = JSON.stringify(parsed.actionEconomy ?? {}, null, 2)
    form.encounterBudget = JSON.stringify(parsed.encounterBudget ?? {}, null, 2)
    form.aiGuidance = JSON.stringify(parsed.aiGuidance ?? {}, null, 2)
  } catch {
    localError.value = 'Failed to apply template.'
  }
}

async function handleValidate(): Promise<void> {
  localError.value = null
  validationResult.value = null
  if (!definitionJson.value) {
    localError.value = 'Invalid JSON in one or more sections.'
    return
  }
  try {
    validationResult.value = await store.validate(definitionJson.value)
  } catch {
    localError.value = 'Validation request failed.'
  }
}

async function handleSave(): Promise<void> {
  localError.value = null
  validationResult.value = null

  if (!form.metadata.name.trim()) {
    localError.value = 'Name is required.'
    activeTab.value = 'metadata'
    return
  }

  if (!definitionJson.value) {
    localError.value = 'Invalid JSON in one or more sections. Please fix before saving.'
    return
  }

  // Validate before save
  try {
    const result = await store.validate(definitionJson.value)
    validationResult.value = result
    if (!result.isValid) {
      localError.value = 'Validation failed. Fix errors before saving.'
      return
    }
  } catch {
    localError.value = 'Validation request failed.'
    return
  }

  saving.value = true
  try {
    if (isEditMode.value && props.definitionId) {
      await store.update(props.definitionId, definitionJson.value)
      emit('saved', props.definitionId)
    } else {
      const created = await store.create(definitionJson.value)
      emit('saved', created.id)
    }
  } catch {
    localError.value = 'Failed to save definition.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="space-y-4">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <h2 class="text-lg font-semibold text-gray-100">
        {{ isEditMode ? 'Edit Game System' : 'Create Game System' }}
      </h2>
      <div class="flex gap-2">
        <button
          type="button"
          class="rounded-lg border border-gray-700 bg-gray-800 px-4 py-2 text-sm font-medium text-gray-300 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400"
          @click="handleValidate"
        >
          Validate
        </button>
        <button
          type="button"
          :disabled="saving"
          class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50"
          @click="handleSave"
        >
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
        <button
          type="button"
          class="rounded-lg px-4 py-2 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none"
          @click="emit('cancel')"
        >
          Cancel
        </button>
      </div>
    </div>

    <!-- Starter templates -->
    <div v-if="!isEditMode && store.templates.length > 0" class="space-y-2">
      <p class="text-xs font-medium text-gray-400">Start from a template:</p>
      <div class="flex flex-wrap gap-2">
        <button
          v-for="template in store.templates"
          :key="template.id"
          type="button"
          class="rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-300 hover:border-aircane-500 hover:text-gray-100 focus:outline-none focus:ring-1 focus:ring-aircane-400"
          :title="template.description"
          @click="applyTemplate(template.definitionJson)"
        >
          {{ template.name }}
        </button>
      </div>
    </div>

    <!-- Tabs -->
    <div class="border-b border-gray-700">
      <nav class="flex gap-1 overflow-x-auto" role="tablist">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          type="button"
          role="tab"
          :aria-selected="activeTab === tab.id"
          :class="[
            'whitespace-nowrap px-3 py-2 text-sm font-medium focus:outline-none',
            activeTab === tab.id
              ? 'border-b-2 border-aircane-500 text-aircane-400'
              : 'text-gray-400 hover:text-gray-200',
          ]"
          @click="activeTab = tab.id"
        >
          {{ tab.label }}
        </button>
      </nav>
    </div>

    <!-- Tab content -->
    <div class="min-h-[300px]">
      <!-- Metadata tab -->
      <div v-show="activeTab === 'metadata'" class="space-y-4">
        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">
              Name <span class="text-red-400">*</span>
            </label>
            <input
              v-model="form.metadata.name"
              type="text"
              placeholder="e.g. Dungeons & Dragons 5e"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">Version</label>
            <input
              v-model="form.metadata.version"
              type="text"
              placeholder="1.0.0"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">Identifier</label>
            <input
              v-model="form.metadata.identifier"
              type="text"
              placeholder="e.g. dnd-5e-2014"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">Publisher</label>
            <input
              v-model="form.metadata.publisher"
              type="text"
              placeholder="e.g. Wizards of the Coast"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">Genre</label>
            <input
              v-model="form.metadata.genre"
              type="text"
              placeholder="e.g. fantasy"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium text-gray-300">License</label>
            <select
              v-model="form.metadata.license"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
            >
              <option value="user-created">User Created</option>
              <option value="community">Community</option>
              <option value="built-in">Built-in</option>
            </select>
          </div>
        </div>
        <div>
          <label class="mb-1 block text-sm font-medium text-gray-300">Description</label>
          <textarea
            v-model="form.metadata.description"
            rows="3"
            placeholder="Brief description of the game system…"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>

      <!-- Dice Conventions tab -->
      <div v-show="activeTab === 'diceConventions'" class="space-y-4">
        <textarea
          v-model="form.diceConventions"
          rows="12"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
        <DiceConventionPreview
          :definition-id="definitionId"
          :convention-json="form.diceConventions"
        />
      </div>

      <!-- Resolution Rules tab -->
      <div v-show="activeTab === 'resolutionRules'">
        <textarea
          v-model="form.resolutionRules"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Character Schema tab -->
      <div v-show="activeTab === 'characterSchema'">
        <textarea
          v-model="form.characterSchema"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Conditions tab -->
      <div v-show="activeTab === 'conditionSet'">
        <textarea
          v-model="form.conditionSet"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Action Economy tab -->
      <div v-show="activeTab === 'actionEconomy'">
        <textarea
          v-model="form.actionEconomy"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Encounter Budget tab -->
      <div v-show="activeTab === 'encounterBudget'">
        <textarea
          v-model="form.encounterBudget"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- AI Notes tab -->
      <div v-show="activeTab === 'aiGuidance'">
        <textarea
          v-model="form.aiGuidance"
          rows="15"
          class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-2 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>
    </div>

    <!-- Validation results -->
    <div v-if="validationResult" class="space-y-2">
      <p v-if="validationResult.isValid" class="text-sm text-green-400">✓ Definition is valid.</p>
      <div v-else class="space-y-1">
        <p class="text-sm font-medium text-red-400">Validation errors:</p>
        <ul class="list-inside list-disc space-y-0.5">
          <li v-for="(err, idx) in validationResult.errors" :key="idx" class="text-xs text-red-300">
            <span class="font-mono text-red-400">{{ err.fieldPath }}</span>: {{ err.message }}
          </li>
        </ul>
      </div>
    </div>

    <!-- Local error -->
    <p v-if="localError" role="alert" class="text-sm text-red-400">{{ localError }}</p>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useGameSystemStore } from '../stores/useGameSystemStore'
import type { GameSystemDefinitionDetail } from '../types'

const props = defineProps<{
  /** The game system definition ID to display. */
  definitionId: string
}>()

const emit = defineEmits<{
  (e: 'back'): void
  (e: 'edit', id: string): void
}>()

const store = useGameSystemStore()
const definition = computed(() => store.activeDefinition)
const parsedDefinition = ref<Record<string, unknown> | null>(null)

onMounted(async () => {
  await store.fetchById(props.definitionId)
  if (definition.value?.definitionJson) {
    try {
      parsedDefinition.value = JSON.parse(definition.value.definitionJson)
    } catch {
      parsedDefinition.value = null
    }
  }
})

const sections = computed(() => {
  if (!parsedDefinition.value) return []
  const keys = ['metadata', 'diceConventions', 'resolutionRules', 'characterSchema', 'conditionSet', 'actionEconomy', 'encounterBudget', 'aiGuidance']
  return keys
    .filter((key) => parsedDefinition.value?.[key] != null)
    .map((key) => ({
      key,
      label: formatSectionLabel(key),
      content: parsedDefinition.value![key],
    }))
})

function formatSectionLabel(key: string): string {
  const labels: Record<string, string> = {
    metadata: 'Metadata',
    diceConventions: 'Dice Conventions',
    resolutionRules: 'Resolution Rules',
    characterSchema: 'Character Schema',
    conditionSet: 'Condition Set',
    actionEconomy: 'Action Economy',
    encounterBudget: 'Encounter Budget',
    aiGuidance: 'AI Guidance',
  }
  return labels[key] ?? key
}
</script>

<template>
  <div class="space-y-4">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <button
        type="button"
        class="text-sm text-gray-400 hover:text-gray-200 focus:outline-none"
        @click="emit('back')"
      >
        ← Back to list
      </button>
      <button
        v-if="definition && !definition.isBuiltIn"
        type="button"
        class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-medium text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="emit('edit', definition.id)"
      >
        Edit
      </button>
    </div>

    <!-- Loading -->
    <p v-if="store.loading" class="text-sm text-gray-400">Loading definition…</p>

    <!-- Error -->
    <p v-if="store.error" role="alert" class="text-sm text-red-400">{{ store.error }}</p>

    <!-- Detail content -->
    <div v-if="definition" class="space-y-6">
      <!-- Title -->
      <div>
        <h2 class="text-xl font-semibold text-gray-100">{{ definition.name }}</h2>
        <p class="text-sm text-gray-400">
          v{{ definition.version }}
          <span v-if="definition.genre"> · {{ definition.genre }}</span>
          <span v-if="definition.publisher"> · {{ definition.publisher }}</span>
        </p>
        <p v-if="definition.description" class="mt-2 text-sm text-gray-300">{{ definition.description }}</p>
        <div class="mt-2 flex gap-2">
          <span class="rounded bg-gray-700 px-2 py-0.5 text-xs text-gray-300">{{ definition.license }}</span>
          <span v-if="definition.isBuiltIn" class="rounded bg-gray-700 px-2 py-0.5 text-xs text-gray-300">Built-in</span>
        </div>
      </div>

      <!-- Sections -->
      <div v-for="section in sections" :key="section.key" class="space-y-2">
        <h3 class="text-sm font-semibold text-gray-200">{{ section.label }}</h3>
        <pre class="overflow-x-auto rounded-lg border border-gray-700 bg-gray-900 p-3 text-xs text-gray-300">{{ JSON.stringify(section.content, null, 2) }}</pre>
      </div>
    </div>
  </div>
</template>

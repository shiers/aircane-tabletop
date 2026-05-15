<script setup lang="ts">
import { computed } from 'vue'
import type { FormDescriptor, FormSection, FormField, VisibilityCondition } from '../types'

const props = defineProps<{
  /** The form descriptor from the API. */
  descriptor: FormDescriptor
  /** The current character data (reactive object). */
  modelValue: Record<string, unknown>
  /** Whether the form is read-only. */
  readonly?: boolean
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: Record<string, unknown>): void
}>()

function updateField(fieldId: string, value: unknown): void {
  emit('update:modelValue', { ...props.modelValue, [fieldId]: value })
}

function getFieldValue(fieldId: string): unknown {
  return props.modelValue[fieldId] ?? null
}

function isSectionVisible(section: FormSection): boolean {
  if (!section.visibleWhen) return true
  return evaluateCondition(section.visibleWhen)
}

function isFieldVisible(field: FormField): boolean {
  if (!field.visibleWhen) return true
  return evaluateCondition(field.visibleWhen)
}

function evaluateCondition(condition: VisibilityCondition): boolean {
  const value = props.modelValue[condition.field]
  if (condition.in) {
    return condition.in.includes(String(value ?? ''))
  }
  if (condition.equals !== undefined) {
    return value === condition.equals
  }
  return true
}

const visibleSections = computed(() =>
  props.descriptor.sections.filter(isSectionVisible),
)
</script>

<template>
  <div class="space-y-6">
    <div v-if="descriptor.sections.length === 0" class="text-sm text-gray-500">
      No character schema defined. Use freeform editing.
    </div>

    <fieldset
      v-for="section in visibleSections"
      :key="section.id"
      class="space-y-3 rounded-lg border border-gray-700 p-4"
    >
      <legend class="px-2 text-sm font-semibold text-gray-200">{{ section.label }}</legend>

      <template v-for="field in section.fields" :key="field.id">
        <div v-if="isFieldVisible(field)" class="space-y-1">
          <!-- Text field -->
          <template v-if="field.type === 'text' || field.type === 'dice_expression'">
            <label :for="`field-${field.id}`" class="block text-xs font-medium text-gray-300">
              {{ field.label }}
              <span v-if="field.required" class="text-red-400">*</span>
            </label>
            <input
              :id="`field-${field.id}`"
              type="text"
              :value="getFieldValue(field.id) ?? ''"
              :required="field.required"
              :disabled="readonly"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-60"
              @input="updateField(field.id, ($event.target as HTMLInputElement).value)"
            />
          </template>

          <!-- Number field -->
          <template v-else-if="field.type === 'number' || field.type === 'resource_pool'">
            <label :for="`field-${field.id}`" class="block text-xs font-medium text-gray-300">
              {{ field.label }}
              <span v-if="field.required" class="text-red-400">*</span>
              <span v-if="field.type === 'resource_pool' && field.maxField" class="text-gray-500">
                / {{ getFieldValue(field.maxField) ?? '?' }}
              </span>
            </label>
            <input
              :id="`field-${field.id}`"
              type="number"
              :value="getFieldValue(field.id) ?? ''"
              :min="field.min ?? undefined"
              :max="field.max ?? undefined"
              :required="field.required"
              :disabled="readonly"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-60"
              @input="updateField(field.id, Number(($event.target as HTMLInputElement).value))"
            />
          </template>

          <!-- Boolean field -->
          <template v-else-if="field.type === 'boolean'">
            <label class="flex items-center gap-2 text-xs font-medium text-gray-300">
              <input
                type="checkbox"
                :checked="!!getFieldValue(field.id)"
                :disabled="readonly"
                class="rounded border-gray-700 bg-gray-800 text-aircane-500 focus:ring-aircane-500"
                @change="updateField(field.id, ($event.target as HTMLInputElement).checked)"
              />
              {{ field.label }}
            </label>
          </template>

          <!-- Enum field -->
          <template v-else-if="field.type === 'enum'">
            <label :for="`field-${field.id}`" class="block text-xs font-medium text-gray-300">
              {{ field.label }}
              <span v-if="field.required" class="text-red-400">*</span>
            </label>
            <select
              :id="`field-${field.id}`"
              :value="getFieldValue(field.id) ?? ''"
              :disabled="readonly"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-60"
              @change="updateField(field.id, ($event.target as HTMLSelectElement).value)"
            >
              <option value="">Select…</option>
              <option v-for="opt in field.options" :key="opt" :value="opt">{{ opt }}</option>
            </select>
          </template>

          <!-- Calculated field (read-only) -->
          <template v-else-if="field.type === 'calculated'">
            <label :for="`field-${field.id}`" class="block text-xs font-medium text-gray-300">
              {{ field.label }}
              <span class="ml-1 text-xs text-gray-500">(calculated)</span>
            </label>
            <input
              :id="`field-${field.id}`"
              type="text"
              :value="getFieldValue(field.id) ?? '-'"
              disabled
              class="block w-full rounded-lg border border-gray-700 bg-gray-900 px-3 py-1.5 text-sm text-gray-400 disabled:opacity-60"
            />
          </template>

          <!-- List field -->
          <template v-else-if="field.type === 'list'">
            <label :for="`field-${field.id}`" class="block text-xs font-medium text-gray-300">
              {{ field.label }}
            </label>
            <textarea
              :id="`field-${field.id}`"
              :value="Array.isArray(getFieldValue(field.id)) ? (getFieldValue(field.id) as string[]).join('\n') : ''"
              :disabled="readonly"
              rows="3"
              placeholder="One item per line"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-60"
              @input="updateField(field.id, ($event.target as HTMLTextAreaElement).value.split('\n').filter(Boolean))"
            />
          </template>

          <!-- Repeating / Grouped fields (simplified display) -->
          <template v-else-if="field.type === 'repeating' || field.type === 'grouped'">
            <label class="block text-xs font-medium text-gray-300">
              {{ field.label }}
              <span class="ml-1 text-xs text-gray-500">({{ field.type }})</span>
            </label>
            <textarea
              :value="JSON.stringify(getFieldValue(field.id) ?? [], null, 2)"
              :disabled="readonly"
              rows="4"
              class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-1.5 font-mono text-xs text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500 disabled:opacity-60"
              @input="(() => { try { updateField(field.id, JSON.parse(($event.target as HTMLTextAreaElement).value)) } catch {} })()"
            />
          </template>
        </div>
      </template>
    </fieldset>
  </div>
</template>

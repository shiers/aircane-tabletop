// ---------------------------------------------------------------------------
// Game System Definition types
// ---------------------------------------------------------------------------

/** Summary DTO returned from list endpoints. */
export interface GameSystemDefinitionSummary {
  id: string
  identifier: string
  name: string
  version: string
  publisher: string | null
  genre: string | null
  description: string | null
  license: string
  isBuiltIn: boolean
  isActive: boolean
  createdAt: string
  updatedAt: string
}

/** Full detail DTO returned from get-by-id endpoint. */
export interface GameSystemDefinitionDetail extends GameSystemDefinitionSummary {
  schemaVersion: number
  definitionJson: string
}

/** Validation error from the API. */
export interface ValidationError {
  fieldPath: string
  message: string
}

/** Validation result from the validate endpoint. */
export interface ValidationResult {
  isValid: boolean
  errors: ValidationError[]
}

/** Starter template summary. */
export interface StarterTemplate {
  id: string
  name: string
  description: string
  genre: string
  definitionJson: string
}

// ---------------------------------------------------------------------------
// Dice Convention types
// ---------------------------------------------------------------------------

export type DiceConventionType =
  | 'single_die_modifier'
  | 'dice_pool_success'
  | 'fixed_dice_threshold'
  | 'fudge'
  | 'step_dice'
  | 'percentile'
  | 'expression'

export interface DiceConvention {
  type: DiceConventionType
  die?: string
  modifierSources?: string[]
  poolSize?: number
  successThreshold?: number
  description?: string
}

// ---------------------------------------------------------------------------
// Resolution Rule types
// ---------------------------------------------------------------------------

export type ResolutionRuleType =
  | 'target_number'
  | 'opposed'
  | 'degrees_of_success'
  | 'margin'
  | 'threshold_bands'

export interface ThresholdBand {
  name: string
  min: number
  max: number | null
}

export interface ResolutionRule {
  type: ResolutionRuleType
  roll?: string
  comparison?: string
  targetSource?: string
  thresholdBands?: ThresholdBand[]
}

// ---------------------------------------------------------------------------
// Character Schema types
// ---------------------------------------------------------------------------

export type FieldType =
  | 'text'
  | 'number'
  | 'boolean'
  | 'enum'
  | 'dice_expression'
  | 'list'
  | 'repeating'
  | 'resource_pool'
  | 'calculated'
  | 'grouped'

/**
 * Integer-indexed field-type order matching the backend `Aircane.Domain.Enums.CharacterFieldType`
 * enum. The `preview-character-form` endpoint serializes field types as their numeric enum value
 * (no `JsonStringEnumConverter`), so field types arrive over the wire as integers `0–9`. This array
 * is the single source of truth for mapping those integers back to the TS `FieldType` discriminants.
 */
export const WIRE_FIELD_TYPE_ORDER: FieldType[] = [
  'text', // 0 = Text
  'number', // 1 = Number
  'boolean', // 2 = Boolean
  'enum', // 3 = Enum
  'dice_expression', // 4 = DiceExpression
  'list', // 5 = List
  'repeating', // 6 = Repeating
  'resource_pool', // 7 = ResourcePool
  'calculated', // 8 = Calculated
  'grouped', // 9 = Grouped
]

const FIELD_TYPE_SET = new Set<string>(WIRE_FIELD_TYPE_ORDER)

/**
 * Normalize a wire field-type value to a `FieldType` string discriminant.
 * - A value that is already a valid `FieldType` string passes through unchanged.
 * - A finite integer `0–9` maps through {@link WIRE_FIELD_TYPE_ORDER}.
 * - Anything else falls back to `'text'` so an unknown field still renders an input
 *   instead of vanishing.
 */
export function fieldTypeFromWire(raw: unknown): FieldType {
  if (typeof raw === 'string' && FIELD_TYPE_SET.has(raw)) {
    return raw as FieldType
  }
  if (typeof raw === 'number' && Number.isInteger(raw) && raw >= 0 && raw < WIRE_FIELD_TYPE_ORDER.length) {
    return WIRE_FIELD_TYPE_ORDER[raw]
  }
  return 'text'
}

export interface SchemaField {
  id: string
  type: FieldType
  label: string
  required?: boolean
  min?: number
  max?: number
  options?: string[]
  formula?: string
  maxField?: string
  visibleWhen?: VisibilityCondition
  itemSchema?: Record<string, string>
}

export interface SchemaSection {
  id: string
  label: string
  fields: SchemaField[]
  visibleWhen?: VisibilityCondition
}

export interface VisibilityCondition {
  field: string
  in?: string[]
  equals?: string | number | boolean
}

export interface FormDescriptor {
  sections: FormSection[]
}

export interface FormSection {
  id: string
  label: string
  fields: FormField[]
  visibleWhen?: VisibilityCondition
}

export interface FormField {
  id: string
  type: FieldType
  label: string
  required: boolean
  min?: number | null
  max?: number | null
  options?: string[] | null
  formula?: string | null
  maxField?: string | null
  visibleWhen?: VisibilityCondition | null
  itemSchema?: Record<string, string> | null
}

// ---------------------------------------------------------------------------
// Condition types
// ---------------------------------------------------------------------------

export interface ConditionDefinition {
  name: string
  description: string
  effects: ConditionEffect[]
  durationType: string
  endCondition?: string
  stackable: boolean
}

export interface ConditionEffect {
  type: string
  scope: string
  effect: string
}

export interface ActiveCondition {
  name: string
  description: string
  durationType: string
  roundsRemaining?: number | null
  endCondition?: string | null
}

// ---------------------------------------------------------------------------
// Action Economy types
// ---------------------------------------------------------------------------

export type ActionEconomyType =
  | 'named_slots'
  | 'action_points'
  | 'multi_action_penalty'
  | 'freeform'

export interface ActionSlot {
  name: string
  count: number
  label: string
  resetOn?: string
  resource?: string
}

export interface ActionEconomyDefinition {
  type: ActionEconomyType
  turnStructure?: {
    slots: ActionSlot[]
  }
  totalPoints?: number
}

export interface ActionBudget {
  type: ActionEconomyType
  slots: ActionSlotState[]
  totalPoints?: number
  remainingPoints?: number
}

export interface ActionSlotState {
  name: string
  label: string
  total: number
  remaining: number
}

// ---------------------------------------------------------------------------
// Roll Result types (extended for system-aware display)
// ---------------------------------------------------------------------------

export interface SystemAwareRollResult {
  id: string
  formula: string | null
  dieResults: number[]
  modifier: number
  total: number
  successCount?: number | null
  outcomeTier?: string | null
  thresholdBands?: ThresholdBand[] | null
  conventionType?: DiceConventionType | null
  context?: string | null
  createdAt: string
}

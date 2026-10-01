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

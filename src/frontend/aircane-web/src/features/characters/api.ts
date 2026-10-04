import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface CharacterDto {
  id: string
  campaignId: string | null
  ownerParticipantId: string | null
  name: string
  gameSystem: string
  ruleset: string
  level: number
  canonicalJson: string
  currentStateJson: string
  createdAt: string
  updatedAt: string
}

export interface CreateCharacterRequest {
  name: string
  gameSystem: string
  ruleset: string
  level: number
  canonicalJson: string
  campaignId?: string | null
  ownerParticipantId?: string | null
}

export interface UpdateCharacterRequest {
  name?: string
  level?: number
  canonicalJson?: string
  currentStateJson?: string
  ownerParticipantId?: string | null
}

export interface ImportCharacterJsonRequest {
  canonicalJson: string
  gameSystem: string
  ruleset: string
  campaignId?: string | null
  ownerParticipantId?: string | null
  originalFileName?: string | null
  /** Force a specific game system definition (used by the source-adapter import path). */
  gameSystemDefinitionId?: string | null
}

/** Request body for the D&D Beyond URL import endpoint. */
export interface DndBeyondUrlImportRequest {
  characterUrl: string
  gameSystemDefinitionId?: string | null
  campaignId?: string | null
}

export interface CharacterImportResult {
  success: boolean
  character: CharacterDto | null
  errors: string[]
}

// ---------------------------------------------------------------------------
// Canonical character shape (subset used by the form)
// ---------------------------------------------------------------------------

export interface AbilityScores {
  strength: number
  dexterity: number
  constitution: number
  intelligence: number
  wisdom: number
  charisma: number
}

export interface CharacterClass {
  className: string
  level: number
  subclass?: string
  hitDie: number
}

export interface CombatStats {
  armorClass: number
  speed: number
  maxHitPoints: number
  currentHitPoints: number
  temporaryHitPoints: number
  initiative: number
  proficiencyBonus: number
}

export interface CharacterIdentity {
  name: string
  raceOrAncestry?: string
  background?: string
  alignment?: string
}

/** Minimal canonical character shape used by the creation form. */
export interface CanonicalCharacterForm {
  identity: CharacterIdentity
  classes: CharacterClass[]
  abilities: AbilityScores
  combat: CombatStats
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build a default empty canonical character for the creation form. */
export function buildDefaultCanonical(): CanonicalCharacterForm {
  return {
    identity: {
      name: '',
      raceOrAncestry: '',
      background: '',
    },
    classes: [{ className: '', level: 1, hitDie: 8 }],
    abilities: {
      strength: 10,
      dexterity: 10,
      constitution: 10,
      intelligence: 10,
      wisdom: 10,
      charisma: 10,
    },
    combat: {
      armorClass: 10,
      speed: 30,
      maxHitPoints: 8,
      currentHitPoints: 8,
      temporaryHitPoints: 0,
      initiative: 0,
      proficiencyBonus: 2,
    },
  }
}

/** Compute the standard 5e ability modifier for a given score. */
export function abilityModifier(score: number): number {
  return Math.floor((score - 10) / 2)
}

/** Format a modifier as a signed string, e.g. "+3" or "-1". */
export function formatModifier(score: number): string {
  const mod = abilityModifier(score)
  return mod >= 0 ? `+${mod}` : `${mod}`
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/** Create a new character manually. */
export async function createCharacter(request: CreateCharacterRequest): Promise<CharacterDto> {
  const response = await apiClient.post<CharacterDto>('/api/characters', request)
  return response.data
}

/** Get a single character by ID. */
export async function getCharacter(id: string): Promise<CharacterDto> {
  const response = await apiClient.get<CharacterDto>(`/api/characters/${id}`)
  return response.data
}

/** Update a character. */
export async function updateCharacter(id: string, request: UpdateCharacterRequest): Promise<CharacterDto> {
  const response = await apiClient.put<CharacterDto>(`/api/characters/${id}`, request)
  return response.data
}

/** Delete a character by ID. */
export async function deleteCharacter(id: string): Promise<void> {
  await apiClient.delete(`/api/characters/${id}`)
}

/** List characters for a campaign. Pass participantId to scope to a player's own characters. */
export async function listCharacters(campaignId: string, participantId?: string): Promise<CharacterDto[]> {
  const params: Record<string, string> = { campaignId }
  if (participantId) params.participantId = participantId
  const response = await apiClient.get<CharacterDto[]>('/api/characters', { params })
  return response.data
}

/** Import a character from a canonical JSON string. Returns the full result including errors. */
export async function importCharacterFromJson(
  request: ImportCharacterJsonRequest,
): Promise<CharacterImportResult> {
  try {
    const response = await apiClient.post<CharacterDto>('/api/characters/import', request)
    return { success: true, character: response.data, errors: [] }
  } catch (err: unknown) {
    // 422 Unprocessable Entity - schema validation errors
    if (
      err &&
      typeof err === 'object' &&
      'response' in err &&
      err.response &&
      typeof err.response === 'object' &&
      'status' in err.response &&
      err.response.status === 422 &&
      'data' in err.response
    ) {
      const data = err.response.data as CharacterImportResult
      return { success: false, character: null, errors: data.errors ?? [] }
    }
    // 400 Bad Request - malformed JSON or missing fields
    if (
      err &&
      typeof err === 'object' &&
      'response' in err &&
      err.response &&
      typeof err.response === 'object' &&
      'status' in err.response &&
      err.response.status === 400 &&
      'data' in err.response
    ) {
      const data = err.response.data as { detail?: string; title?: string }
      return {
        success: false,
        character: null,
        errors: [data.detail ?? data.title ?? 'Invalid request.'],
      }
    }
    throw err
  }
}

/**
 * Import a character via the source-adapter path. Posts the raw JSON in `canonicalJson` with
 * `gameSystem`/`ruleset` left blank; the backend detects the source (or uses the pinned `source`).
 * Returns the {@link SourceImportResponse} envelope. `source` is only sent when the user overrides
 * auto-detect; `gameSystemDefinitionId` pins the system for Roll20/Generic sheets.
 */
export async function importCharacterFromSource(
  request: ImportCharacterJsonRequest,
  options?: { source?: string; gameSystemDefinitionId?: string },
): Promise<SourceImportResponse> {
  const params: Record<string, string> = {}
  if (options?.source) params.source = options.source
  if (options?.gameSystemDefinitionId) params.gameSystemDefinitionId = options.gameSystemDefinitionId
  const response = await apiClient.post<SourceImportResponse>('/api/characters/import', request, {
    params,
  })
  return response.data
}

/** Import a character from a D&D Beyond character URL. Returns the {@link SourceImportResponse}. */
export async function importCharacterFromDndBeyondUrl(
  request: DndBeyondUrlImportRequest,
): Promise<SourceImportResponse> {
  const response = await apiClient.post<SourceImportResponse>(
    '/api/characters/import/dndbeyond-url',
    request,
  )
  return response.data
}

// ---------------------------------------------------------------------------
// Field review DTOs
// ---------------------------------------------------------------------------

export interface UnmappedFieldDto {
  sourceFieldName: string
  sourceValue: string
  suggestedCanonicalField: string | null
  confidence: number
  /** Force the review UI to require explicit confirmation of this field. */
  requiresReview?: boolean
}

export interface CharacterFieldReviewDto {
  characterId: string
  reviewRequired: true
  unmappedFields: UnmappedFieldDto[]
  warnings: string[]
  // ── Review-scoped signals from the source-adapter import path. ──
  /** Game-system definition id the review form should render against. */
  gameSystemDefinitionId?: string | null
  /** ApplyMapping path → stringified value, used to seed the review form. */
  mappedFields?: Record<string, string>
  /** True for Roll20/Generic when the user must pick a system before mapping. */
  requiresGameSystemSelection?: boolean
}

/**
 * Envelope returned by the source-adapter import path (extended POST /api/characters/import and the
 * D&D Beyond URL import). Carries the detected-source / confidence / ruleset ENVELOPE signals plus
 * the {@link CharacterFieldReviewDto} that drives the review UI. `detectedSource` is a string.
 */
export interface SourceImportResponse {
  detectedSource: string
  confidence: string
  requiresSourceConfirmation: boolean
  ruleset: string | null
  rulesetRequiresConfirmation: boolean
  review: CharacterFieldReviewDto
}

export interface FieldMappingEntry {
  sourceFieldName: string
  canonicalFieldPath: string
  value: string
}

export interface ApplyFieldMappingsRequest {
  mappings: FieldMappingEntry[]
}

/**
 * The list of canonical field paths the review UI exposes in the dropdown.
 * Mirrors the paths handled by CharacterService.ApplyMapping on the backend.
 */
export const CANONICAL_FIELD_OPTIONS: { label: string; value: string }[] = [
  { label: 'Name', value: 'identity.name' },
  { label: 'Race / Ancestry', value: 'race' },
  { label: 'Class', value: 'class' },
  { label: 'Level', value: 'level' },
  { label: 'Background', value: 'identity.background' },
  { label: 'Alignment', value: 'identity.alignment' },
  { label: 'Experience Points', value: 'experiencePoints' },
  { label: 'Strength', value: 'abilities.strength' },
  { label: 'Dexterity', value: 'abilities.dexterity' },
  { label: 'Constitution', value: 'abilities.constitution' },
  { label: 'Intelligence', value: 'abilities.intelligence' },
  { label: 'Wisdom', value: 'abilities.wisdom' },
  { label: 'Charisma', value: 'abilities.charisma' },
  { label: 'Armor Class', value: 'combat.armorClass' },
  { label: 'Hit Points (current)', value: 'combat.hitPoints' },
  { label: 'Max Hit Points', value: 'combat.maxHitPoints' },
  { label: 'Speed', value: 'combat.speed' },
  { label: 'Initiative', value: 'combat.initiative' },
  { label: 'Proficiency Bonus', value: 'combat.proficiencyBonus' },
  { label: 'Passive Perception', value: 'passivePerception' },
]

/** Apply user-confirmed field mappings to a character. Returns the updated character. */
export async function applyFieldMappings(
  characterId: string,
  request: ApplyFieldMappingsRequest,
): Promise<CharacterDto> {
  const response = await apiClient.put<CharacterDto>(
    `/api/characters/${characterId}/field-mappings`,
    request,
  )
  return response.data
}

// ---------------------------------------------------------------------------
// Character template DTOs
// ---------------------------------------------------------------------------

export interface TemplateFieldDefinition {
  fieldName: string
  canonicalFieldPath: string
  dataType: 'string' | 'int' | 'bool'
  displayGroup: string
  isRequired: boolean
  defaultValue: string | null
  sourceCoordinate: string | null
}

export interface CharacterTemplateDto {
  id: string
  name: string
  gameSystem: string
  ruleset: string
  fieldDefinitions: TemplateFieldDefinition[]
  createdAt: string
  updatedAt: string
}

export interface SaveTemplateRequest {
  name: string
  gameSystem: string
  ruleset: string
  fieldDefinitions: TemplateFieldDefinition[]
  sourceCharacterId?: string | null
}

// ---------------------------------------------------------------------------
// Template API functions
// ---------------------------------------------------------------------------

/** Save a character import mapping as a reusable template. */
export async function saveTemplate(request: SaveTemplateRequest): Promise<CharacterTemplateDto> {
  const response = await apiClient.post<CharacterTemplateDto>('/api/characters/templates', request)
  return response.data
}

/** List all saved character templates, optionally filtered by game system. */
export async function listTemplates(gameSystem?: string): Promise<CharacterTemplateDto[]> {
  const params: Record<string, string> = {}
  if (gameSystem) params.gameSystem = gameSystem
  const response = await apiClient.get<CharacterTemplateDto[]>('/api/characters/templates', { params })
  return response.data
}

/** Get a single character template by ID. */
export async function getTemplate(id: string): Promise<CharacterTemplateDto> {
  const response = await apiClient.get<CharacterTemplateDto>(`/api/characters/templates/${id}`)
  return response.data
}

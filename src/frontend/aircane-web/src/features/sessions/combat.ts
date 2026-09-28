import apiClient from '@/shared/api/client'

// ---------------------------------------------------------------------------
// Combat / encounter types (mirror the backend EncounterStateDto)
// ---------------------------------------------------------------------------

export interface ConditionInstanceDto {
  name: string
  remainingRounds: number | null
  appliedOnRound: number
  endCondition: string | null
}

export interface DeathSaveStateDto {
  successes: number
  failures: number
  isStable: boolean
  isDead: boolean
}

export interface CombatantDto {
  id: string
  name: string
  isPlayerCharacter: boolean
  currentHp: number
  maxHp: number
  temporaryHp: number
  initiative: number
  isDowned: boolean
  isDead: boolean
  conditions: ConditionInstanceDto[]
  deathSaves: DeathSaveStateDto | null
}

export interface EncounterStateDto {
  encounterId: string
  isActive: boolean
  round: number
  turnIndex: number
  activeCombatantId: string | null
  initiativeOrder: string[]
  combatants: CombatantDto[]
}

/** Payload of the `CombatTurnChanged` SignalR event (camelCase C# record). */
export interface CombatTurnChangedNotification {
  sessionId: string
  activeCreatureId: string | null
  activeCreatureName: string | null
  round: number
  turnIndex: number
}

/**
 * Rough HP band shown to players for other combatants, so exact enemy HP is not leaked.
 * Healthy > 50%, Bloodied 1–50%, Critical <= ~25% but still up, Down at 0.
 */
export type HpBand = 'Healthy' | 'Bloodied' | 'Critical' | 'Down'

export function hpBand(current: number, max: number): HpBand {
  if (current <= 0) return 'Down'
  if (max <= 0) return 'Healthy'
  const pct = current / max
  if (pct <= 0.25) return 'Critical'
  if (pct <= 0.5) return 'Bloodied'
  return 'Healthy'
}

// ---------------------------------------------------------------------------
// API functions (host/DM combat controls)
// ---------------------------------------------------------------------------

/** Fetch the current live encounter for a session. Returns null when no encounter is active (204). */
export async function getEncounter(sessionId: string): Promise<EncounterStateDto | null> {
  const response = await apiClient.get<EncounterStateDto>(`/api/sessions/${sessionId}/combat`)
  // Axios resolves 204 with empty data.
  return response.status === 204 || !response.data ? null : response.data
}

/** Advance combat to the next combatant in initiative order. */
export async function advanceTurn(sessionId: string): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/advance-turn`,
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** Roll initiative for the encounter using per-combatant values. */
export async function rollInitiative(
  sessionId: string,
  initiative: Record<string, number>,
): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/roll-initiative`,
    { initiative },
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** Apply damage to a combatant. */
export async function applyDamage(
  sessionId: string,
  targetId: string,
  amount: number,
): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/apply-damage`,
    { targetId, amount },
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** Apply healing to a combatant. */
export async function applyHealing(
  sessionId: string,
  targetId: string,
  amount: number,
): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/apply-healing`,
    { targetId, amount },
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** Apply a condition to a combatant. */
export async function applyCondition(
  sessionId: string,
  targetId: string,
  conditionName: string,
  remainingRounds?: number | null,
): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/apply-condition`,
    { targetId, conditionName, remainingRounds: remainingRounds ?? null },
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** Remove a condition from a combatant. */
export async function removeCondition(
  sessionId: string,
  targetId: string,
  conditionName: string,
): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/remove-condition`,
    { targetId, conditionName },
  )
  return response.status === 204 || !response.data ? null : response.data
}

/** End the active encounter. */
export async function endEncounter(sessionId: string): Promise<EncounterStateDto | null> {
  const response = await apiClient.post<EncounterStateDto>(
    `/api/sessions/${sessionId}/combat/end`,
  )
  return response.status === 204 || !response.data ? null : response.data
}

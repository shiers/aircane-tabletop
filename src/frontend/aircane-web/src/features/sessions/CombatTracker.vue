<script setup lang="ts">
/**
 * CombatTracker - initiative + HP tracker for a live encounter.
 *
 * Two modes, controlled by the `readonly` prop:
 *  - Host/DM (readonly=false): full HP values, action slots, Next Turn, and quick-action bar
 *    (apply damage/healing, apply/remove condition). Actions are emitted to the parent, which
 *    calls the combat API and updates the encounter.
 *  - Player (readonly=true): read-only initiative strip. The player's own character shows full
 *    HP; other combatants show only a rough band (Healthy / Bloodied / Critical / Down) so exact
 *    enemy HP is not leaked.
 */
import { ref, computed } from 'vue'
import {
  type EncounterStateDto,
  type CombatantDto,
  hpBand,
  type HpBand,
} from './combat'

const props = withDefaults(
  defineProps<{
    encounter: EncounterStateDto | null
    /** True for the player-facing read-only view. */
    readonly?: boolean
    /** Whether to show per-turn action slots (hidden for freeform systems). */
    showActionSlots?: boolean
    /** Ordered action-slot labels for the active combatant (e.g. ["Action", "Bonus Action", "Reaction"]). */
    actionSlots?: string[]
    /** The current player's own character id (read-only view shows full HP for this one). */
    ownCharacterId?: string | null
  }>(),
  {
    readonly: false,
    showActionSlots: false,
    actionSlots: () => [],
    ownCharacterId: null,
  },
)

const emit = defineEmits<{
  (e: 'advance-turn'): void
  (e: 'apply-damage', targetId: string, amount: number): void
  (e: 'apply-healing', targetId: string, amount: number): void
  (e: 'apply-condition', targetId: string, conditionName: string): void
  (e: 'remove-condition', targetId: string, conditionName: string): void
}>()

// ---------------------------------------------------------------------------
// Derived state
// ---------------------------------------------------------------------------

const isActive = computed(() => !!props.encounter?.isActive)

/** Combatants ordered by the encounter's initiative order (falling back to array order). */
const orderedCombatants = computed<CombatantDto[]>(() => {
  const enc = props.encounter
  if (!enc) return []
  if (enc.initiativeOrder.length === 0) return enc.combatants
  const byId = new Map(enc.combatants.map((c) => [c.id, c]))
  const ordered: CombatantDto[] = []
  for (const id of enc.initiativeOrder) {
    const c = byId.get(id)
    if (c) ordered.push(c)
  }
  // Append any combatants missing from the initiative order.
  for (const c of enc.combatants) {
    if (!enc.initiativeOrder.includes(c.id)) ordered.push(c)
  }
  return ordered
})

const activeId = computed(() => props.encounter?.activeCombatantId ?? null)

function isActiveTurn(c: CombatantDto): boolean {
  return c.id === activeId.value
}

/** Whether to reveal exact HP for a combatant (always in host view; own character in player view). */
function revealsExactHp(c: CombatantDto): boolean {
  if (!props.readonly) return true
  return props.ownCharacterId != null && c.id === props.ownCharacterId
}

function hpPercent(c: CombatantDto): number {
  if (c.maxHp <= 0) return 0
  return Math.max(0, Math.min(100, Math.round((c.currentHp / c.maxHp) * 100)))
}

function bandFor(c: CombatantDto): HpBand {
  return hpBand(c.currentHp, c.maxHp)
}

const bandClasses: Record<HpBand, string> = {
  Healthy: 'bg-green-500',
  Bloodied: 'bg-yellow-500',
  Critical: 'bg-red-500',
  Down: 'bg-gray-600',
}

const bandLabelClasses: Record<HpBand, string> = {
  Healthy: 'text-green-400',
  Bloodied: 'text-yellow-400',
  Critical: 'text-red-400',
  Down: 'text-gray-400',
}

// ---------------------------------------------------------------------------
// Quick-action popover state (host only)
// ---------------------------------------------------------------------------

type QuickAction = 'damage' | 'healing' | 'condition' | 'remove-condition'
const openAction = ref<QuickAction | null>(null)
const actionTargetId = ref<string>('')
const actionAmount = ref<number>(0)
const actionConditionName = ref<string>('')

function openQuickAction(action: QuickAction): void {
  openAction.value = action
  actionTargetId.value = orderedCombatants.value[0]?.id ?? ''
  actionAmount.value = 0
  actionConditionName.value = ''
}

function closeQuickAction(): void {
  openAction.value = null
}

function submitQuickAction(): void {
  const target = actionTargetId.value
  if (!target) return
  switch (openAction.value) {
    case 'damage':
      if (actionAmount.value >= 0) emit('apply-damage', target, actionAmount.value)
      break
    case 'healing':
      if (actionAmount.value >= 0) emit('apply-healing', target, actionAmount.value)
      break
    case 'condition':
      if (actionConditionName.value.trim()) emit('apply-condition', target, actionConditionName.value.trim())
      break
    case 'remove-condition':
      if (actionConditionName.value.trim()) emit('remove-condition', target, actionConditionName.value.trim())
      break
  }
  closeQuickAction()
}
</script>

<template>
  <section
    v-if="isActive"
    class="rounded-xl border border-surface-700/50 bg-surface-850 p-6"
    aria-label="Combat tracker"
  >
    <header class="mb-3 flex items-center justify-between">
      <h3 class="text-sm font-medium uppercase tracking-wider text-gray-400">Combat</h3>
      <span class="rounded-md bg-surface-700/50 px-2 py-0.5 text-xs font-medium text-gray-300">
        Round {{ encounter?.round }}
      </span>
    </header>

    <!-- Initiative list (announced live so turn changes are read out). -->
    <ol class="space-y-2" role="list" aria-live="polite">
      <li
        v-for="c in orderedCombatants"
        :key="c.id"
        class="rounded-lg border px-3 py-2 transition-colors"
        :class="
          isActiveTurn(c)
            ? 'border-aircane-400 ring-1 ring-aircane-400/60 bg-surface-800'
            : 'border-surface-700/50 bg-surface-900/40'
        "
        :data-combatant-id="c.id"
        :aria-current="isActiveTurn(c) ? 'true' : undefined"
      >
        <div class="flex items-center justify-between gap-3">
          <div class="flex items-center gap-2 min-w-0">
            <span
              v-if="isActiveTurn(c)"
              class="inline-block h-2 w-2 shrink-0 rounded-full bg-aircane-400"
              aria-hidden="true"
            />
            <span class="truncate font-medium text-white">{{ c.name }}</span>
            <span v-if="c.isPlayerCharacter" class="text-xs text-aircane-400">(PC)</span>
          </div>

          <!-- Exact HP (host, or own character) -->
          <div v-if="revealsExactHp(c)" class="shrink-0 text-sm tabular-nums text-gray-300">
            {{ c.currentHp }}/{{ c.maxHp }}
            <span v-if="c.temporaryHp > 0" class="text-aircane-400">(+{{ c.temporaryHp }})</span>
          </div>
          <!-- Rough band (players, other combatants) -->
          <div v-else class="shrink-0 text-xs font-medium" :class="bandLabelClasses[bandFor(c)]">
            {{ bandFor(c) }}
          </div>
        </div>

        <!-- HP bar: exact width for revealed, band-coloured full bar otherwise -->
        <div class="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-surface-700/60">
          <div
            class="h-full rounded-full transition-all"
            :class="bandClasses[bandFor(c)]"
            :style="{ width: revealsExactHp(c) ? hpPercent(c) + '%' : '100%' }"
          />
        </div>

        <!-- Conditions -->
        <div v-if="c.conditions.length > 0" class="mt-1.5 flex flex-wrap gap-1">
          <span
            v-for="cond in c.conditions"
            :key="cond.name"
            class="rounded-full bg-surface-700/60 px-2 py-0.5 text-xs text-gray-300"
          >
            {{ cond.name }}<template v-if="cond.remainingRounds != null"> ({{ cond.remainingRounds }}r)</template>
          </span>
        </div>

        <!-- Downed / dying badges -->
        <p v-if="c.deathSaves && !c.deathSaves.isDead && !c.deathSaves.isStable" class="mt-1 text-xs text-red-300">
          Dying — death saves {{ c.deathSaves.successes }}/3 success, {{ c.deathSaves.failures }}/3 fail
        </p>
        <p v-else-if="c.deathSaves?.isDead" class="mt-1 text-xs font-semibold text-red-400">Dead</p>
        <p v-else-if="c.deathSaves?.isStable" class="mt-1 text-xs text-yellow-400">Stable (downed)</p>
        <p v-else-if="c.isDowned" class="mt-1 text-xs text-gray-400">
          {{ c.isPlayerCharacter ? 'Downed' : 'Defeated' }}
        </p>
      </li>
    </ol>

    <!-- Action slots for the active combatant (host only, structured systems only) -->
    <div
      v-if="!readonly && showActionSlots && actionSlots.length > 0"
      class="mt-3 flex flex-wrap gap-2"
      aria-label="Active combatant action slots"
    >
      <span
        v-for="slot in actionSlots"
        :key="slot"
        class="rounded-md border border-surface-700/50 bg-surface-900/50 px-2 py-1 text-xs text-gray-300"
      >
        {{ slot }}
      </span>
    </div>

    <!-- Host controls: Next Turn + quick-action bar -->
    <div v-if="!readonly" class="mt-4 space-y-3">
      <button
        type="button"
        class="w-full rounded-md bg-aircane-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400"
        @click="emit('advance-turn')"
      >
        Next Turn
      </button>

      <div class="flex flex-wrap gap-2">
        <button
          type="button"
          class="rounded-md border border-surface-700/50 px-3 py-1.5 text-sm font-medium text-gray-200 transition-colors hover:bg-surface-700/50"
          @click="openQuickAction('damage')"
        >
          Apply Damage
        </button>
        <button
          type="button"
          class="rounded-md border border-surface-700/50 px-3 py-1.5 text-sm font-medium text-gray-200 transition-colors hover:bg-surface-700/50"
          @click="openQuickAction('healing')"
        >
          Apply Healing
        </button>
        <button
          type="button"
          class="rounded-md border border-surface-700/50 px-3 py-1.5 text-sm font-medium text-gray-200 transition-colors hover:bg-surface-700/50"
          @click="openQuickAction('condition')"
        >
          Apply Condition
        </button>
        <button
          type="button"
          class="rounded-md border border-surface-700/50 px-3 py-1.5 text-sm font-medium text-gray-200 transition-colors hover:bg-surface-700/50"
          @click="openQuickAction('remove-condition')"
        >
          Remove Condition
        </button>
      </div>

      <!-- Quick-action popover -->
      <div
        v-if="openAction"
        class="rounded-lg border border-surface-700/50 bg-surface-900/70 p-3"
        role="dialog"
        aria-label="Combat quick action"
      >
        <div class="space-y-2">
          <label class="block text-xs font-medium text-gray-400">
            Target
            <select
              v-model="actionTargetId"
              class="mt-1 w-full rounded-md border border-surface-700/50 bg-surface-850 px-2 py-1 text-sm text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
            >
              <option v-for="c in orderedCombatants" :key="c.id" :value="c.id">{{ c.name }}</option>
            </select>
          </label>

          <label
            v-if="openAction === 'damage' || openAction === 'healing'"
            class="block text-xs font-medium text-gray-400"
          >
            Amount
            <input
              v-model.number="actionAmount"
              type="number"
              min="0"
              class="mt-1 w-full rounded-md border border-surface-700/50 bg-surface-850 px-2 py-1 text-sm text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
            />
          </label>

          <label
            v-else
            class="block text-xs font-medium text-gray-400"
          >
            Condition
            <input
              v-model="actionConditionName"
              type="text"
              placeholder="e.g. Poisoned"
              class="mt-1 w-full rounded-md border border-surface-700/50 bg-surface-850 px-2 py-1 text-sm text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
            />
          </label>

          <div class="flex justify-end gap-2 pt-1">
            <button
              type="button"
              class="rounded-md px-3 py-1.5 text-sm text-gray-400 hover:text-gray-200"
              @click="closeQuickAction"
            >
              Cancel
            </button>
            <button
              type="button"
              class="rounded-md bg-aircane-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-aircane-500"
              @click="submitQuickAction"
            >
              Apply
            </button>
          </div>
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { reactive, ref, watch, computed } from 'vue'
import {
  buildDefaultCanonical,
  formatModifier,
  type CharacterDto,
  type CreateCharacterRequest,
  type UpdateCharacterRequest,
  type CanonicalCharacterForm,
} from '../api'

// ---------------------------------------------------------------------------
// Props / emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** When provided the form operates in edit mode. */
  character?: CharacterDto
  /** Optional campaign ID to associate the character with. */
  campaignId?: string
}>()

const emit = defineEmits<{
  (e: 'submit', payload: CreateCharacterRequest | UpdateCharacterRequest): void
  (e: 'cancel'): void
}>()

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

function parseCanonical(json?: string): CanonicalCharacterForm {
  if (!json) return buildDefaultCanonical()
  try {
    return JSON.parse(json) as CanonicalCharacterForm
  } catch {
    return buildDefaultCanonical()
  }
}

const existingCanonical = computed(() => parseCanonical(props.character?.canonicalJson))

const form = reactive({
  name: props.character?.name ?? '',
  gameSystem: props.character?.gameSystem ?? 'D&D 5e',
  ruleset: props.character?.ruleset ?? '2014',
  level: props.character?.level ?? 1,
  // Identity
  raceOrAncestry: existingCanonical.value.identity?.raceOrAncestry ?? '',
  background: existingCanonical.value.identity?.background ?? '',
  // Class
  className: existingCanonical.value.classes?.[0]?.className ?? '',
  hitDie: existingCanonical.value.classes?.[0]?.hitDie ?? 8,
  // Ability scores
  strength: existingCanonical.value.abilities?.strength ?? 10,
  dexterity: existingCanonical.value.abilities?.dexterity ?? 10,
  constitution: existingCanonical.value.abilities?.constitution ?? 10,
  intelligence: existingCanonical.value.abilities?.intelligence ?? 10,
  wisdom: existingCanonical.value.abilities?.wisdom ?? 10,
  charisma: existingCanonical.value.abilities?.charisma ?? 10,
  // Combat
  armorClass: existingCanonical.value.combat?.armorClass ?? 10,
  maxHitPoints: existingCanonical.value.combat?.maxHitPoints ?? 8,
  speed: existingCanonical.value.combat?.speed ?? 30,
})

const localError = ref<string | null>(null)
const submitting = ref(false)

// Sync form when the character prop changes
watch(
  () => props.character,
  (c) => {
    if (!c) return
    const canonical = parseCanonical(c.canonicalJson)
    form.name = c.name
    form.gameSystem = c.gameSystem
    form.ruleset = c.ruleset
    form.level = c.level
    form.raceOrAncestry = canonical.identity?.raceOrAncestry ?? ''
    form.background = canonical.identity?.background ?? ''
    form.className = canonical.classes?.[0]?.className ?? ''
    form.hitDie = canonical.classes?.[0]?.hitDie ?? 8
    form.strength = canonical.abilities?.strength ?? 10
    form.dexterity = canonical.abilities?.dexterity ?? 10
    form.constitution = canonical.abilities?.constitution ?? 10
    form.intelligence = canonical.abilities?.intelligence ?? 10
    form.wisdom = canonical.abilities?.wisdom ?? 10
    form.charisma = canonical.abilities?.charisma ?? 10
    form.armorClass = canonical.combat?.armorClass ?? 10
    form.maxHitPoints = canonical.combat?.maxHitPoints ?? 8
    form.speed = canonical.combat?.speed ?? 30
  },
)

// ---------------------------------------------------------------------------
// Ability score helpers
// ---------------------------------------------------------------------------

const abilityFields = [
  { key: 'strength' as const, label: 'STR' },
  { key: 'dexterity' as const, label: 'DEX' },
  { key: 'constitution' as const, label: 'CON' },
  { key: 'intelligence' as const, label: 'INT' },
  { key: 'wisdom' as const, label: 'WIS' },
  { key: 'charisma' as const, label: 'CHA' },
]

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

function buildCanonicalJson(): string {
  const canonical: CanonicalCharacterForm = {
    identity: {
      name: form.name.trim(),
      raceOrAncestry: form.raceOrAncestry.trim() || undefined,
      background: form.background.trim() || undefined,
    },
    classes: [
      {
        className: form.className.trim(),
        level: form.level,
        hitDie: form.hitDie,
      },
    ],
    abilities: {
      strength: form.strength,
      dexterity: form.dexterity,
      constitution: form.constitution,
      intelligence: form.intelligence,
      wisdom: form.wisdom,
      charisma: form.charisma,
    },
    combat: {
      armorClass: form.armorClass,
      speed: form.speed,
      maxHitPoints: form.maxHitPoints,
      currentHitPoints: form.maxHitPoints,
      temporaryHitPoints: 0,
      initiative: Math.floor((form.dexterity - 10) / 2),
      proficiencyBonus: form.level < 5 ? 2 : form.level < 9 ? 3 : form.level < 13 ? 4 : form.level < 17 ? 5 : 6,
    },
  }
  return JSON.stringify(canonical)
}

function validate(): string | null {
  if (!form.name.trim()) return 'Character name is required.'
  if (!form.className.trim()) return 'Class is required.'
  if (!form.gameSystem.trim()) return 'Game system is required.'
  if (!form.ruleset.trim()) return 'Ruleset is required.'
  if (form.level < 1 || form.level > 20) return 'Level must be between 1 and 20.'
  for (const { key, label } of abilityFields) {
    const val = form[key]
    if (val < 1 || val > 30) return `${label} must be between 1 and 30.`
  }
  if (form.armorClass < 0) return 'AC must be 0 or greater.'
  if (form.maxHitPoints < 1) return 'HP must be at least 1.'
  if (form.speed < 0) return 'Speed must be 0 or greater.'
  return null
}

async function handleSubmit(): Promise<void> {
  localError.value = null

  const validationError = validate()
  if (validationError) {
    localError.value = validationError
    return
  }

  submitting.value = true
  try {
    const canonicalJson = buildCanonicalJson()

    if (props.character) {
      // Edit mode — only send changed fields
      const payload: UpdateCharacterRequest = {
        name: form.name.trim(),
        level: form.level,
        canonicalJson,
      }
      emit('submit', payload)
    } else {
      // Create mode
      const payload: CreateCharacterRequest = {
        name: form.name.trim(),
        gameSystem: form.gameSystem.trim(),
        ruleset: form.ruleset.trim(),
        level: form.level,
        canonicalJson,
        campaignId: props.campaignId ?? null,
      }
      emit('submit', payload)
    }
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <form novalidate class="space-y-6" @submit.prevent="handleSubmit">

    <!-- ── Identity ─────────────────────────────────────────────────────── -->
    <fieldset class="space-y-4">
      <legend class="text-sm font-semibold uppercase tracking-wider text-gray-400">Identity</legend>

      <!-- Name -->
      <div>
        <label for="char-name" class="mb-1 block text-sm font-medium text-gray-300">
          Name <span aria-hidden="true" class="text-red-400">*</span>
        </label>
        <input
          id="char-name"
          v-model="form.name"
          type="text"
          required
          aria-required="true"
          placeholder="e.g. Aldric Stonehammer"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        />
      </div>

      <!-- Race / Ancestry + Background -->
      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label for="char-race" class="mb-1 block text-sm font-medium text-gray-300">
            Race / Ancestry
          </label>
          <input
            id="char-race"
            v-model="form.raceOrAncestry"
            type="text"
            placeholder="e.g. Human, Elf, Dwarf"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="char-background" class="mb-1 block text-sm font-medium text-gray-300">
            Background
          </label>
          <input
            id="char-background"
            v-model="form.background"
            type="text"
            placeholder="e.g. Soldier, Sage"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>
    </fieldset>

    <!-- ── Class & Level ─────────────────────────────────────────────────── -->
    <fieldset class="space-y-4">
      <legend class="text-sm font-semibold uppercase tracking-wider text-gray-400">Class &amp; Level</legend>

      <div class="grid gap-4 sm:grid-cols-3">
        <!-- Class -->
        <div class="sm:col-span-2">
          <label for="char-class" class="mb-1 block text-sm font-medium text-gray-300">
            Class <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-class"
            v-model="form.className"
            type="text"
            required
            aria-required="true"
            placeholder="e.g. Fighter, Wizard"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <!-- Level -->
        <div>
          <label for="char-level" class="mb-1 block text-sm font-medium text-gray-300">
            Level <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-level"
            v-model.number="form.level"
            type="number"
            min="1"
            max="20"
            required
            aria-required="true"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>

      <!-- Game System + Ruleset (only shown in create mode) -->
      <div v-if="!character" class="grid gap-4 sm:grid-cols-2">
        <div>
          <label for="char-game-system" class="mb-1 block text-sm font-medium text-gray-300">
            Game System <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-game-system"
            v-model="form.gameSystem"
            type="text"
            required
            aria-required="true"
            placeholder="e.g. D&D 5e"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="char-ruleset" class="mb-1 block text-sm font-medium text-gray-300">
            Ruleset <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-ruleset"
            v-model="form.ruleset"
            type="text"
            required
            aria-required="true"
            placeholder="e.g. 2014"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>
    </fieldset>

    <!-- ── Ability Scores ────────────────────────────────────────────────── -->
    <fieldset class="space-y-3">
      <legend class="text-sm font-semibold uppercase tracking-wider text-gray-400">Ability Scores</legend>

      <div class="grid grid-cols-3 gap-3 sm:grid-cols-6">
        <div
          v-for="ability in abilityFields"
          :key="ability.key"
          class="flex flex-col items-center gap-1"
        >
          <label
            :for="`char-${ability.key}`"
            class="text-xs font-bold uppercase tracking-wider text-gray-400"
          >
            {{ ability.label }}
          </label>
          <input
            :id="`char-${ability.key}`"
            v-model.number="form[ability.key]"
            type="number"
            min="1"
            max="30"
            class="w-full rounded-lg border border-gray-700 bg-gray-800 px-2 py-2 text-center text-sm font-semibold text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
          <span class="text-xs text-gray-500" aria-label="modifier">
            {{ formatModifier(form[ability.key]) }}
          </span>
        </div>
      </div>
    </fieldset>

    <!-- ── Combat Stats ──────────────────────────────────────────────────── -->
    <fieldset class="space-y-4">
      <legend class="text-sm font-semibold uppercase tracking-wider text-gray-400">Combat</legend>

      <div class="grid gap-4 sm:grid-cols-3">
        <!-- AC -->
        <div>
          <label for="char-ac" class="mb-1 block text-sm font-medium text-gray-300">
            Armor Class <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-ac"
            v-model.number="form.armorClass"
            type="number"
            min="0"
            required
            aria-required="true"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <!-- HP -->
        <div>
          <label for="char-hp" class="mb-1 block text-sm font-medium text-gray-300">
            Max HP <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-hp"
            v-model.number="form.maxHitPoints"
            type="number"
            min="1"
            required
            aria-required="true"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <!-- Speed -->
        <div>
          <label for="char-speed" class="mb-1 block text-sm font-medium text-gray-300">
            Speed (ft.) <span aria-hidden="true" class="text-red-400">*</span>
          </label>
          <input
            id="char-speed"
            v-model.number="form.speed"
            type="number"
            min="0"
            step="5"
            required
            aria-required="true"
            class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </div>
    </fieldset>

    <!-- ── Error ─────────────────────────────────────────────────────────── -->
    <p v-if="localError" role="alert" class="text-sm text-red-400">{{ localError }}</p>

    <!-- ── Actions ───────────────────────────────────────────────────────── -->
    <div class="flex items-center gap-3">
      <button
        type="submit"
        :disabled="submitting"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {{ character ? 'Save Changes' : 'Create Character' }}
      </button>

      <button
        type="button"
        class="rounded-lg px-4 py-2.5 text-sm font-medium text-gray-400 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
        @click="emit('cancel')"
      >
        Cancel
      </button>
    </div>
  </form>
</template>

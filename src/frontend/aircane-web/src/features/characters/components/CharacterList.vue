<script setup lang="ts">
import { ref } from 'vue'
import { useCharacterStore } from '../store'
import { abilityModifier, type CharacterDto } from '../api'
import AbilityScoreTile from './AbilityScoreTile.vue'
import RoleBadge from './RoleBadge.vue'

const emit = defineEmits<{
  (e: 'edit', character: CharacterDto): void
  (e: 'view', character: CharacterDto): void
}>()

const store = useCharacterStore()
const deletingId = ref<string | null>(null)

function parseLevel(character: CharacterDto): number {
  return character.level
}

function parseClass(character: CharacterDto): string {
  try {
    const canonical = JSON.parse(character.canonicalJson)
    const classes: Array<{ className: string; level: number }> = canonical?.classes ?? []
    if (classes.length === 0) return '-'
    return classes.map((c) => `${c.className} ${c.level}`).join(' / ')
  } catch {
    return '-'
  }
}

function parseRace(character: CharacterDto): string {
  try {
    const canonical = JSON.parse(character.canonicalJson)
    return canonical?.identity?.raceOrAncestry ?? '-'
  } catch {
    return '-'
  }
}

/** Primary class name used to pick a portrait. */
function primaryClassName(character: CharacterDto): string {
  try {
    const canonical = JSON.parse(character.canonicalJson)
    return canonical?.classes?.[0]?.className ?? ''
  } catch {
    return ''
  }
}

/**
 * Map a class name to one of the four portrait arts.
 * Fighter/Warrior->fighter, Rogue/Ranger->rogue, Cleric/Paladin->cleric,
 * Wizard/Sorcerer/Druid->wizard. Any unmatched class falls back to fighter.
 */
function portraitUrl(character: CharacterDto): string {
  const name = primaryClassName(character).toLowerCase()
  let key = 'fighter'
  if (/(rogue|ranger)/.test(name)) key = 'rogue'
  else if (/(cleric|paladin)/.test(name)) key = 'cleric'
  else if (/(wizard|sorcerer|druid)/.test(name)) key = 'wizard'
  else key = 'fighter' // Fighter/Warrior + fallback for anything unmatched
  return `/assets/characters/character-portrait-${key}.png`
}

/** The six ability scores for compact display on the card. */
function abilities(
  character: CharacterDto,
): { abbr: string; score: number; modifier: number }[] {
  const abbrs: { key: string; abbr: string }[] = [
    { key: 'strength', abbr: 'STR' },
    { key: 'dexterity', abbr: 'DEX' },
    { key: 'constitution', abbr: 'CON' },
    { key: 'intelligence', abbr: 'INT' },
    { key: 'wisdom', abbr: 'WIS' },
    { key: 'charisma', abbr: 'CHA' },
  ]
  let scores: Record<string, number> = {}
  try {
    const canonical = JSON.parse(character.canonicalJson)
    scores = canonical?.abilities ?? {}
  } catch {
    scores = {}
  }
  return abbrs.map(({ key, abbr }) => {
    const score = typeof scores[key] === 'number' ? scores[key] : 10
    return { abbr, score, modifier: abilityModifier(score) }
  })
}

/** Players own a participant; NPCs do not. */
function role(character: CharacterDto): 'Player' | 'NPC' {
  return character.ownerParticipantId ? 'Player' : 'NPC'
}

async function handleDelete(character: CharacterDto): Promise<void> {
  if (!confirm(`Delete "${character.name}"? This cannot be undone.`)) return
  deletingId.value = character.id
  try {
    await store.deleteCharacter(character.id)
  } finally {
    deletingId.value = null
  }
}
</script>

<template>
  <section aria-labelledby="characters-list-heading">
    <h2 id="characters-list-heading" class="mb-4 text-lg font-semibold text-white">
      Characters
    </h2>

    <!-- Loading skeleton -->
    <div
      v-if="store.loading && store.characters.length === 0"
      class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3"
      aria-busy="true"
      aria-label="Loading characters"
    >
      <div v-for="n in 3" :key="n" class="h-80 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!store.loading && store.characters.length === 0"
      class="flex flex-col items-center py-12 text-center text-gray-400"
    >
      <img
        src="/assets/characters/character-empty-state-ornament.png"
        alt=""
        aria-hidden="true"
        class="mb-4 w-full max-w-xs"
      />
      <p class="text-sm">No characters yet. Create one above to get started.</p>
    </div>

    <!-- Character cards -->
    <ul v-else class="grid list-none gap-4 p-0 sm:grid-cols-2 lg:grid-cols-3">
      <li v-for="character in store.characters" :key="character.id" class="character-card">
        <!-- Framed card: portrait (upper 65%) + stats (lower 35%).
             The frame art bakes in its own border — no CSS border is added. -->
        <div class="card-media">
          <img
            :src="portraitUrl(character)"
            alt=""
            aria-hidden="true"
            class="portrait"
          />
          <img
            src="/assets/characters/character-card-frame.png"
            alt=""
            aria-hidden="true"
            class="frame"
          />
          <div class="card-stats">
            <p class="card-name">{{ character.name }}</p>
            <p class="card-meta">{{ parseRace(character) }} · {{ parseClass(character) }}</p>
            <div class="card-badges">
              <RoleBadge :role="role(character)" />
              <span class="level-pill">Lv {{ parseLevel(character) }}</span>
            </div>
          </div>
        </div>

        <!-- Ability scores -->
        <div class="card-abilities">
          <AbilityScoreTile
            v-for="ability in abilities(character)"
            :key="ability.abbr"
            :abbr="ability.abbr"
            :score="ability.score"
            :modifier="ability.modifier"
          />
        </div>

        <!-- Actions -->
        <div class="card-actions">
          <button
            :aria-label="`View ${character.name}`"
            class="rounded px-2 py-1 text-xs font-medium text-gray-400 hover:bg-gray-800 hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-gray-500"
            @click="emit('view', character)"
          >
            View
          </button>

          <button
            :aria-label="`Edit ${character.name}`"
            class="rounded px-2 py-1 text-xs font-medium text-blue-400 hover:bg-gray-800 hover:text-blue-300 focus:outline-none focus:ring-2 focus:ring-blue-500"
            @click="emit('edit', character)"
          >
            Edit
          </button>

          <button
            :disabled="deletingId === character.id"
            :aria-label="`Delete ${character.name}`"
            class="rounded px-2 py-1 text-xs font-medium text-red-400 hover:bg-gray-800 hover:text-red-300 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
            @click="handleDelete(character)"
          >
            <span v-if="deletingId === character.id">Deleting…</span>
            <span v-else>Delete</span>
          </button>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.character-card {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

/* Framed media area: portrait occupies the upper 65%, stats the lower 35%. */
.card-media {
  position: relative;
  width: 100%;
  aspect-ratio: 3 / 4;
  overflow: hidden;
  border-radius: var(--border-radius-md);
}

.portrait {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 65%;
  object-fit: cover;
}

/* Frame overlay — baked-in border; sits on top, non-interactive. */
.frame {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: fill;
  pointer-events: none;
}

.card-stats {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 35%;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 4px;
  padding: 0 12px;
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

.card-name {
  font-size: 1rem;
  font-weight: 700;
  color: #fff;
}

.card-meta {
  font-size: 0.75rem;
  color: #e5e7eb;
}

.card-badges {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 2px;
}

.level-pill {
  padding: 2px 10px;
  border-radius: var(--border-radius-pill);
  background: var(--color-bg-panel);
  border: var(--border-gold-dim);
  font-size: 12px;
  font-weight: 600;
  color: var(--color-gold);
}

.card-abilities {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 6px;
}

.card-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
}
</style>

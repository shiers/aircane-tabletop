<script setup lang="ts">
const props = defineProps<{
  /** Ability abbreviation, e.g. "STR". */
  abbr: string
  /** Raw ability score, e.g. 16. */
  score: number
  /** Ability modifier; rendered with an explicit sign (+N / -N / +0). */
  modifier: number
}>()

const signedModifier = (): string =>
  props.modifier >= 0 ? `+${props.modifier}` : `${props.modifier}`
</script>

<template>
  <div class="ability-tile">
    <span class="abbr">{{ abbr }}</span>
    <span class="score">{{ score }}</span>
    <span class="modifier">{{ signedModifier() }}</span>
  </div>
</template>

<style scoped>
.ability-tile {
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  min-width: 64px;
  padding: 10px 8px;
  /* Art already bakes in its own border — do not add a CSS border. */
  background-image: url('/assets/characters/ability-score-tile.png');
  background-size: 100% 100%;
  background-repeat: no-repeat;
  border-radius: var(--border-radius-sm);
  text-shadow: 0 2px 6px rgba(0, 0, 0, 0.7);
}

.abbr {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--color-gold);
}

.score {
  font-size: 20px;
  font-weight: 700;
  line-height: 1;
  color: #fff;
}

.modifier {
  font-size: 12px;
  font-weight: 600;
  color: #e5e7eb;
}
</style>

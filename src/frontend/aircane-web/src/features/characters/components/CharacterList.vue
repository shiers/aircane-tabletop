<script setup lang="ts">
import { ref } from 'vue'
import { useCharacterStore } from '../store'
import { type CharacterDto } from '../api'

const emit = defineEmits<{
  (e: 'edit', character: CharacterDto): void
  (e: 'view', character: CharacterDto): void
}>()

const store = useCharacterStore()
const deletingId = ref<string | null>(null)

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

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
      class="space-y-2"
      aria-busy="true"
      aria-label="Loading characters"
    >
      <div v-for="n in 3" :key="n" class="h-14 animate-pulse rounded-lg bg-gray-800" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="!store.loading && store.characters.length === 0"
      class="rounded-xl border border-dashed border-gray-700 py-16 text-center text-gray-500"
    >
      <p class="text-sm">No characters yet. Create one above to get started.</p>
    </div>

    <!-- Character table -->
    <div v-else class="overflow-x-auto rounded-xl border border-gray-800">
      <table class="w-full text-left text-sm text-gray-300" aria-label="Characters">
        <thead class="border-b border-gray-800 bg-gray-900 text-xs uppercase tracking-wider text-gray-500">
          <tr>
            <th scope="col" class="px-4 py-3">Name</th>
            <th scope="col" class="px-4 py-3">Race / Ancestry</th>
            <th scope="col" class="px-4 py-3">Class</th>
            <th scope="col" class="px-4 py-3">Level</th>
            <th scope="col" class="px-4 py-3">System</th>
            <th scope="col" class="px-4 py-3">Created</th>
            <th scope="col" class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-800 bg-gray-950">
          <tr
            v-for="character in store.characters"
            :key="character.id"
            class="hover:bg-gray-900"
          >
            <!-- Name -->
            <td class="px-4 py-3 font-medium text-white">{{ character.name }}</td>

            <!-- Race / Ancestry -->
            <td class="px-4 py-3 text-gray-400">{{ parseRace(character) }}</td>

            <!-- Class -->
            <td class="px-4 py-3">{{ parseClass(character) }}</td>

            <!-- Level -->
            <td class="px-4 py-3">{{ parseLevel(character) }}</td>

            <!-- System -->
            <td class="px-4 py-3 text-gray-400">
              {{ [character.gameSystem, character.ruleset].filter(Boolean).join(' · ') }}
            </td>

            <!-- Created date -->
            <td class="px-4 py-3 text-gray-400">{{ formatDate(character.createdAt) }}</td>

            <!-- Actions -->
            <td class="px-4 py-3 text-right">
              <div class="flex items-center justify-end gap-2">
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
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

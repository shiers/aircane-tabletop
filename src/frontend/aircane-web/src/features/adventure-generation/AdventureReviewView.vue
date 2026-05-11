<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import {
  getAdventureDraft,
  approveAdventure,
  type AdventureDraft,
} from './api'

const props = defineProps<{
  adventureId: string
}>()

const router = useRouter()
const draft = ref<AdventureDraft | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const approving = ref(false)
const approveSuccess = ref(false)
const activeSection = ref<string>('pitch')

const sections = computed(() => {
  if (!draft.value) return []
  const s: { key: string; label: string; available: boolean }[] = [
    { key: 'pitch', label: 'Pitch', available: !!draft.value.pitch },
    { key: 'outline', label: 'Outline', available: !!draft.value.outline },
    { key: 'scenes', label: 'Scenes', available: !!draft.value.scenes?.length },
    { key: 'npcs', label: 'NPCs', available: !!draft.value.npcs?.length },
    { key: 'encounters', label: 'Encounters', available: !!draft.value.encounters?.length },
    { key: 'treasure', label: 'Treasure', available: !!draft.value.treasure },
    { key: 'clues', label: 'Clues & Secrets', available: !!draft.value.clues },
  ]
  return s
})

const isApproved = computed(() => draft.value?.status === 'Approved')
const isDraft = computed(() => draft.value?.status === 'Draft')

onMounted(async () => {
  await loadDraft()
})

async function loadDraft() {
  loading.value = true
  error.value = null
  try {
    draft.value = await getAdventureDraft(props.adventureId)
  } catch (e: any) {
    const status = e?.response?.status
    if (status === 404) {
      error.value = 'Adventure not found.'
    } else {
      error.value = e?.response?.data?.detail ?? e?.message ?? 'Failed to load adventure draft.'
    }
  } finally {
    loading.value = false
  }
}

async function handleApprove() {
  if (!draft.value || approving.value) return
  approving.value = true
  error.value = null
  try {
    await approveAdventure(props.adventureId)
    approveSuccess.value = true
    draft.value = { ...draft.value, status: 'Approved' }
  } catch (e: any) {
    const data = e?.response?.data
    error.value = data?.detail ?? e?.message ?? 'Failed to approve adventure.'
  } finally {
    approving.value = false
  }
}

function goBack() {
  router.push({ name: 'adventure-generate' })
}

function getDifficultyColor(difficulty: string): string {
  switch (difficulty.toLowerCase()) {
    case 'easy': return 'text-green-400'
    case 'medium': return 'text-yellow-400'
    case 'hard': return 'text-orange-400'
    case 'deadly': return 'text-red-400'
    default: return 'text-gray-400'
  }
}

function getSceneTypeColor(sceneType: string): string {
  switch (sceneType.toLowerCase()) {
    case 'combat': return 'bg-red-900/50 text-red-300 border-red-700'
    case 'exploration': return 'bg-green-900/50 text-green-300 border-green-700'
    case 'roleplay': return 'bg-blue-900/50 text-blue-300 border-blue-700'
    case 'puzzle': return 'bg-purple-900/50 text-purple-300 border-purple-700'
    default: return 'bg-gray-900/50 text-gray-300 border-gray-700'
  }
}
</script>

<template>
  <div class="min-h-screen bg-gray-950 text-gray-100">
    <!-- Header -->
    <header class="border-b border-gray-800 px-6 py-4">
      <div class="mx-auto flex max-w-5xl items-center justify-between">
        <div class="flex items-center gap-3">
          <button
            @click="goBack"
            class="text-sm text-gray-400 hover:text-white"
          >
            ← Back
          </button>
          <h1 class="text-xl font-bold tracking-tight text-aircane-400">
            {{ draft?.title ?? 'Adventure Review' }}
          </h1>
        </div>
        <div class="flex items-center gap-3">
          <!-- Status badge -->
          <span
            v-if="draft"
            class="rounded-full px-3 py-1 text-xs font-medium"
            :class="{
              'bg-yellow-900/50 text-yellow-300 border border-yellow-700': isDraft,
              'bg-green-900/50 text-green-300 border border-green-700': isApproved,
              'bg-gray-800 text-gray-400 border border-gray-700': !isDraft && !isApproved,
            }"
          >
            {{ draft.status }}
          </span>
          <!-- Approve button -->
          <button
            v-if="isDraft"
            @click="handleApprove"
            :disabled="approving"
            class="rounded-md bg-green-700 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-green-600 focus:outline-none focus:ring-2 focus:ring-green-400 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <span v-if="approving" class="flex items-center gap-2">
              <svg class="h-4 w-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Approving…
            </span>
            <span v-else>✓ Approve Adventure</span>
          </button>
        </div>
      </div>
    </header>

    <main class="mx-auto max-w-5xl px-6 py-8">
      <!-- Loading state -->
      <div v-if="loading" class="flex items-center justify-center py-20">
        <svg class="h-8 w-8 animate-spin text-aircane-400" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
        </svg>
        <span class="ml-3 text-gray-400">Loading adventure draft…</span>
      </div>

      <!-- Error state -->
      <div v-else-if="error" class="rounded-md border border-red-800 bg-red-950 px-4 py-3 text-red-300" role="alert">
        <p class="font-medium">Error</p>
        <p class="mt-1 text-sm">{{ error }}</p>
        <button @click="loadDraft" class="mt-3 text-sm text-red-200 underline hover:text-white">
          Try again
        </button>
      </div>

      <!-- Approve success banner -->
      <output
        v-if="approveSuccess"
        class="mb-6 block rounded-md border border-green-800 bg-green-950 px-4 py-3 text-green-300"
      >
        <p class="font-medium">Adventure approved!</p>
        <p class="mt-1 text-sm">This adventure is now playable and can be assigned to a campaign.</p>
      </output>

      <!-- Draft content -->
      <div v-if="draft && !loading" class="flex gap-6">
        <!-- Section navigation -->
        <nav class="w-48 shrink-0" aria-label="Adventure sections">
          <ul class="space-y-1">
            <li v-for="section in sections" :key="section.key">
              <button
                @click="activeSection = section.key"
                :disabled="!section.available"
                class="w-full rounded-md px-3 py-2 text-left text-sm transition-colors"
                :class="{
                  'bg-aircane-900/50 text-aircane-300 font-medium': activeSection === section.key,
                  'text-gray-400 hover:bg-gray-800 hover:text-gray-200': activeSection !== section.key && section.available,
                  'text-gray-600 cursor-not-allowed': !section.available,
                }"
              >
                {{ section.label }}
                <span v-if="!section.available" class="ml-1 text-xs text-gray-600">(empty)</span>
              </button>
            </li>
          </ul>
        </nav>

        <!-- Section content -->
        <div class="min-w-0 flex-1">
          <!-- Pitch Section -->
          <section v-if="activeSection === 'pitch' && draft.pitch" aria-labelledby="pitch-heading">
            <h2 id="pitch-heading" class="mb-4 text-lg font-semibold text-gray-200">Pitch</h2>
            <div class="space-y-4 rounded-lg border border-gray-800 bg-gray-900 p-6">
              <div>
                <h3 class="text-sm font-medium text-gray-400">Title</h3>
                <p class="mt-1 text-lg font-semibold text-gray-100">{{ draft.pitch.title }}</p>
              </div>
              <div>
                <h3 class="text-sm font-medium text-gray-400">Hook</h3>
                <p class="mt-1 text-gray-200 italic">{{ draft.pitch.hook }}</p>
              </div>
              <div>
                <h3 class="text-sm font-medium text-gray-400">Summary</h3>
                <p class="mt-1 text-gray-300">{{ draft.pitch.summary }}</p>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button
                disabled
                class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50"
                title="Regenerate section (coming soon)"
              >
                ↻ Regenerate
              </button>
              <button
                disabled
                class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50"
                title="Edit section (coming soon)"
              >
                ✎ Edit
              </button>
            </div>
          </section>

          <!-- Outline Section -->
          <section v-if="activeSection === 'outline' && draft.outline" aria-labelledby="outline-heading">
            <h2 id="outline-heading" class="mb-4 text-lg font-semibold text-gray-200">Outline</h2>
            <div class="space-y-3">
              <div
                v-for="(scene, index) in draft.outline.sceneSummaries"
                :key="index"
                class="rounded-lg border border-gray-800 bg-gray-900 p-4"
              >
                <div class="flex items-center gap-3">
                  <span class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-gray-800 text-xs font-mono text-gray-400">
                    {{ index + 1 }}
                  </span>
                  <div class="min-w-0 flex-1">
                    <div class="flex items-center gap-2">
                      <h3 class="font-medium text-gray-200">{{ scene.title }}</h3>
                      <span
                        class="rounded-full border px-2 py-0.5 text-xs"
                        :class="getSceneTypeColor(scene.sceneType)"
                      >
                        {{ scene.sceneType }}
                      </span>
                    </div>
                    <p class="mt-1 text-sm text-gray-400">{{ scene.description }}</p>
                  </div>
                </div>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- Scenes Section -->
          <section v-if="activeSection === 'scenes' && draft.scenes?.length" aria-labelledby="scenes-heading">
            <h2 id="scenes-heading" class="mb-4 text-lg font-semibold text-gray-200">Scenes</h2>
            <div class="space-y-4">
              <details
                v-for="scene in draft.scenes"
                :key="scene.sceneId"
                class="group rounded-lg border border-gray-800 bg-gray-900"
              >
                <summary class="cursor-pointer px-4 py-3 hover:bg-gray-800/50">
                  <div class="inline-flex items-center gap-2">
                    <span class="font-medium text-gray-200">{{ scene.title }}</span>
                    <span
                      class="rounded-full border px-2 py-0.5 text-xs"
                      :class="getSceneTypeColor(scene.sceneType)"
                    >
                      {{ scene.sceneType }}
                    </span>
                  </div>
                </summary>
                <div class="border-t border-gray-800 px-4 py-4 space-y-4">
                  <div>
                    <h4 class="text-sm font-medium text-gray-400">Description</h4>
                    <p class="mt-1 text-sm text-gray-300">{{ scene.description }}</p>
                  </div>
                  <div v-if="scene.readAloudText">
                    <h4 class="text-sm font-medium text-gray-400">Read Aloud</h4>
                    <blockquote class="mt-1 border-l-2 border-aircane-600 pl-3 text-sm italic text-gray-200">
                      {{ scene.readAloudText }}
                    </blockquote>
                  </div>
                  <div v-if="scene.dmNotes">
                    <h4 class="text-sm font-medium text-gray-400">DM Notes</h4>
                    <p class="mt-1 rounded bg-gray-800 px-3 py-2 text-sm text-yellow-200">
                      {{ scene.dmNotes }}
                    </p>
                  </div>
                  <div v-if="scene.connectsTo.length">
                    <h4 class="text-sm font-medium text-gray-400">Connects To</h4>
                    <div class="mt-1 flex flex-wrap gap-1">
                      <span
                        v-for="conn in scene.connectsTo"
                        :key="conn"
                        class="rounded bg-gray-800 px-2 py-0.5 text-xs text-gray-300"
                      >
                        {{ conn }}
                      </span>
                    </div>
                  </div>
                </div>
              </details>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- NPCs Section -->
          <section v-if="activeSection === 'npcs' && draft.npcs?.length" aria-labelledby="npcs-heading">
            <h2 id="npcs-heading" class="mb-4 text-lg font-semibold text-gray-200">NPCs</h2>
            <div class="grid gap-4 sm:grid-cols-2">
              <div
                v-for="npc in draft.npcs"
                :key="npc.name"
                class="rounded-lg border border-gray-800 bg-gray-900 p-4"
              >
                <div class="flex items-start justify-between">
                  <h3 class="font-medium text-gray-200">{{ npc.name }}</h3>
                  <span class="rounded-full bg-gray-800 px-2 py-0.5 text-xs text-gray-400">
                    {{ npc.role }}
                  </span>
                </div>
                <p class="mt-2 text-sm text-gray-400">{{ npc.personality }}</p>
                <div class="mt-3 flex items-center justify-between text-xs text-gray-500">
                  <span>{{ npc.statsSummary }}</span>
                  <span v-if="npc.faction" class="text-aircane-400">{{ npc.faction }}</span>
                </div>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- Encounters Section -->
          <section v-if="activeSection === 'encounters' && draft.encounters?.length" aria-labelledby="encounters-heading">
            <h2 id="encounters-heading" class="mb-4 text-lg font-semibold text-gray-200">Encounters</h2>
            <div class="space-y-4">
              <div
                v-for="encounter in draft.encounters"
                :key="encounter.title"
                class="rounded-lg border border-gray-800 bg-gray-900 p-4"
              >
                <div class="flex items-center justify-between">
                  <h3 class="font-medium text-gray-200">{{ encounter.title }}</h3>
                  <span class="text-sm font-medium" :class="getDifficultyColor(encounter.difficulty)">
                    {{ encounter.difficulty }}
                  </span>
                </div>
                <p class="mt-1 text-xs text-gray-500">Scene: {{ encounter.sceneId }}</p>
                <!-- Enemies -->
                <div class="mt-3">
                  <h4 class="text-xs font-medium text-gray-400 uppercase tracking-wide">Enemies</h4>
                  <ul class="mt-1 space-y-1">
                    <li
                      v-for="enemy in encounter.enemies"
                      :key="enemy.name"
                      class="flex items-center justify-between text-sm"
                    >
                      <span class="text-gray-300">{{ enemy.name }}</span>
                      <span class="text-gray-500">
                        ×{{ enemy.count }} · CR {{ enemy.challengeRating }}
                      </span>
                    </li>
                  </ul>
                </div>
                <!-- Tactics -->
                <div class="mt-3">
                  <h4 class="text-xs font-medium text-gray-400 uppercase tracking-wide">Tactics</h4>
                  <p class="mt-1 text-sm text-gray-400">{{ encounter.tactics }}</p>
                </div>
                <!-- Environment -->
                <div v-if="encounter.environment" class="mt-3">
                  <h4 class="text-xs font-medium text-gray-400 uppercase tracking-wide">Environment</h4>
                  <p class="mt-1 text-sm text-gray-400">{{ encounter.environment }}</p>
                </div>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- Treasure Section -->
          <section v-if="activeSection === 'treasure' && draft.treasure" aria-labelledby="treasure-heading">
            <h2 id="treasure-heading" class="mb-4 text-lg font-semibold text-gray-200">Treasure</h2>
            <div class="rounded-lg border border-gray-800 bg-gray-900 p-6 space-y-6">
              <div class="flex items-center gap-2">
                <span class="text-2xl">💰</span>
                <span class="text-lg font-semibold text-yellow-300">{{ draft.treasure.goldTotal }} gp total</span>
              </div>

              <!-- Mundane Items -->
              <div v-if="draft.treasure.items.length">
                <h3 class="text-sm font-medium text-gray-400 mb-2">Items</h3>
                <ul class="space-y-2">
                  <li
                    v-for="item in draft.treasure.items"
                    :key="item.name"
                    class="flex items-start justify-between rounded bg-gray-800 px-3 py-2"
                  >
                    <div>
                      <span class="text-sm font-medium text-gray-200">{{ item.name }}</span>
                      <p class="text-xs text-gray-400">{{ item.description }}</p>
                    </div>
                    <span v-if="item.value" class="shrink-0 text-xs text-yellow-400">{{ item.value }} gp</span>
                  </li>
                </ul>
              </div>

              <!-- Magic Items -->
              <div v-if="draft.treasure.magicItems.length">
                <h3 class="text-sm font-medium text-gray-400 mb-2">Magic Items</h3>
                <ul class="space-y-2">
                  <li
                    v-for="item in draft.treasure.magicItems"
                    :key="item.name"
                    class="flex items-start justify-between rounded border border-purple-800/50 bg-purple-950/30 px-3 py-2"
                  >
                    <div>
                      <span class="text-sm font-medium text-purple-200">{{ item.name }}</span>
                      <p class="text-xs text-gray-400">{{ item.description }}</p>
                    </div>
                    <span v-if="item.value" class="shrink-0 text-xs text-yellow-400">{{ item.value }} gp</span>
                  </li>
                </ul>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- Clues & Secrets Section -->
          <section v-if="activeSection === 'clues' && draft.clues" aria-labelledby="clues-heading">
            <h2 id="clues-heading" class="mb-4 text-lg font-semibold text-gray-200">Clues & Secrets</h2>
            <div class="space-y-6">
              <!-- Secrets -->
              <div v-if="draft.clues.secrets.length">
                <h3 class="text-sm font-medium text-gray-400 mb-3">Secrets</h3>
                <div class="space-y-3">
                  <div
                    v-for="secret in draft.clues.secrets"
                    :key="secret.title"
                    class="rounded-lg border border-gray-800 bg-gray-900 p-4"
                  >
                    <h4 class="font-medium text-gray-200">{{ secret.title }}</h4>
                    <p class="mt-1 text-sm text-gray-400">{{ secret.content }}</p>
                    <div class="mt-2 flex gap-3 text-xs text-gray-500">
                      <span>Scene: {{ secret.sceneId }}</span>
                      <span>Discovery: {{ secret.discoveryMethod }}</span>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Handouts -->
              <div v-if="draft.clues.handouts.length">
                <h3 class="text-sm font-medium text-gray-400 mb-3">Handouts</h3>
                <div class="space-y-3">
                  <div
                    v-for="handout in draft.clues.handouts"
                    :key="handout.title"
                    class="rounded-lg border border-gray-800 bg-gray-900 p-4"
                  >
                    <h4 class="font-medium text-gray-200">{{ handout.title }}</h4>
                    <p class="mt-1 text-sm text-gray-300 italic border-l-2 border-gray-700 pl-3">
                      {{ handout.content }}
                    </p>
                    <p class="mt-2 text-xs text-gray-500">Scene: {{ handout.sceneId }}</p>
                  </div>
                </div>
              </div>

              <!-- Fail-Forward Paths -->
              <div v-if="draft.clues.failForwardPaths.length">
                <h3 class="text-sm font-medium text-gray-400 mb-3">Fail-Forward Paths</h3>
                <div class="space-y-3">
                  <div
                    v-for="path in draft.clues.failForwardPaths"
                    :key="path.trigger"
                    class="rounded-lg border border-gray-800 bg-gray-900 p-4"
                  >
                    <div>
                      <h4 class="text-xs font-medium text-gray-400 uppercase tracking-wide">If players get stuck</h4>
                      <p class="mt-1 text-sm text-gray-300">{{ path.trigger }}</p>
                    </div>
                    <div class="mt-2">
                      <h4 class="text-xs font-medium text-gray-400 uppercase tracking-wide">Then</h4>
                      <p class="mt-1 text-sm text-green-300">{{ path.resolution }}</p>
                    </div>
                    <p class="mt-2 text-xs text-gray-500">Scene: {{ path.sceneId }}</p>
                  </div>
                </div>
              </div>
            </div>
            <div class="mt-4 flex gap-2">
              <button disabled class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-xs text-gray-400 cursor-not-allowed opacity-50" title="Regenerate section (coming soon)">
                ↻ Regenerate
              </button>
            </div>
          </section>

          <!-- Empty state for unavailable sections -->
          <div
            v-if="!sections.find(s => s.key === activeSection)?.available && !loading"
            class="flex flex-col items-center justify-center py-16 text-gray-500"
          >
            <p class="text-sm">This section has no content yet.</p>
          </div>
        </div>
      </div>
    </main>
  </div>
</template>

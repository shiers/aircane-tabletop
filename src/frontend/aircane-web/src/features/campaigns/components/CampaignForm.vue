<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { AiRole, AiAuthority, type CampaignDto, type CreateCampaignRequest, type UpdateCampaignRequest } from '../api'
import GameSystemSelector from '@/features/game-systems/components/GameSystemSelector.vue'

// ---------------------------------------------------------------------------
// Props / emits
// ---------------------------------------------------------------------------

const props = defineProps<{
  /** When provided the form operates in edit mode. */
  campaign?: CampaignDto
}>()

const emit = defineEmits<{
  (e: 'submit', payload: CreateCampaignRequest | UpdateCampaignRequest): void
  (e: 'cancel'): void
}>()

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

const form = reactive({
  name: props.campaign?.name ?? '',
  gameSystem: props.campaign?.gameSystem ?? '',
  ruleset: props.campaign?.ruleset ?? '',
  gameSystemDefinitionId: (props.campaign as CampaignDto & { gameSystemDefinitionId?: string | null })?.gameSystemDefinitionId ?? null as string | null,
  aiRole: props.campaign?.aiRole ?? AiRole.Assistant,
  aiAuthority: props.campaign?.aiAuthority ?? AiAuthority.SuggestOnly,
})

const localError = ref<string | null>(null)
const submitting = ref(false)

// Sync form when the campaign prop changes (e.g. parent swaps the edited item)
watch(
  () => props.campaign,
  (c) => {
    if (!c) return
    form.name = c.name
    form.gameSystem = c.gameSystem
    form.ruleset = c.ruleset
    form.gameSystemDefinitionId = (c as CampaignDto & { gameSystemDefinitionId?: string | null })?.gameSystemDefinitionId ?? null
    form.aiRole = c.aiRole
    form.aiAuthority = c.aiAuthority
  },
)

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

const aiRoleOptions: { label: string; value: AiRole }[] = [
  { label: 'Assistant', value: AiRole.Assistant },
  { label: 'Co-DM', value: AiRole.CoDm },
  { label: 'Full DM', value: AiRole.FullDm },
  { label: 'Hybrid', value: AiRole.Hybrid },
]

const aiAuthorityOptions: { label: string; value: AiAuthority }[] = [
  { label: 'Suggest Only', value: AiAuthority.SuggestOnly },
  { label: 'Ask Before Applying', value: AiAuthority.AskBeforeApplying },
  { label: 'Auto-Apply Safe Actions', value: AiAuthority.AutoApplySafeActions },
  { label: 'Full Session Control', value: AiAuthority.FullSessionControl },
]

// ---------------------------------------------------------------------------
// Submit
// ---------------------------------------------------------------------------

async function handleSubmit(): Promise<void> {
  localError.value = null

  if (!form.name.trim()) {
    localError.value = 'Campaign name is required.'
    return
  }
  if (!form.gameSystemDefinitionId) {
    localError.value = 'Game system is required.'
    return
  }

  submitting.value = true
  try {
    emit('submit', {
      name: form.name.trim(),
      gameSystem: form.gameSystem.trim(),
      ruleset: form.ruleset.trim(),
      gameSystemDefinitionId: form.gameSystemDefinitionId,
      aiRole: form.aiRole,
      aiAuthority: form.aiAuthority,
    })
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <form novalidate class="space-y-4" @submit.prevent="handleSubmit">
    <!-- Name -->
    <div>
      <label for="campaign-name" class="mb-1 block text-sm font-medium text-gray-300">
        Name <span aria-hidden="true" class="text-red-400">*</span>
      </label>
      <input
        id="campaign-name"
        v-model="form.name"
        type="text"
        required
        aria-required="true"
        placeholder="e.g. The Lost Mines"
        class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 placeholder-gray-500 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
      />
    </div>

    <!-- Game System Definition Selector -->
    <GameSystemSelector v-model="form.gameSystemDefinitionId" />

    <!-- AI Role + AI Authority -->
    <div class="grid gap-4 sm:grid-cols-2">
      <div>
        <label for="campaign-ai-role" class="mb-1 block text-sm font-medium text-gray-300">
          AI Role
        </label>
        <select
          id="campaign-ai-role"
          v-model.number="form.aiRole"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        >
          <option v-for="opt in aiRoleOptions" :key="opt.value" :value="opt.value">
            {{ opt.label }}
          </option>
        </select>
      </div>

      <div>
        <label for="campaign-ai-authority" class="mb-1 block text-sm font-medium text-gray-300">
          AI Authority
        </label>
        <select
          id="campaign-ai-authority"
          v-model.number="form.aiAuthority"
          class="block w-full rounded-lg border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-gray-100 focus:border-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-500"
        >
          <option v-for="opt in aiAuthorityOptions" :key="opt.value" :value="opt.value">
            {{ opt.label }}
          </option>
        </select>
      </div>
    </div>

    <!-- Error -->
    <p v-if="localError" role="alert" class="text-sm text-red-400">{{ localError }}</p>

    <!-- Actions -->
    <div class="flex items-center gap-3">
      <button
        type="submit"
        :disabled="submitting"
        class="inline-flex items-center gap-2 rounded-lg bg-aircane-600 px-5 py-2.5 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {{ campaign ? 'Save Changes' : 'Create Campaign' }}
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

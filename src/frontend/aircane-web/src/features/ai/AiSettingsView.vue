<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  AiProviderType,
  getAiConfig,
  updateAiConfig,
  testAiConnection,
  getProviders,
  getAvailableModels,
  type UpdateAiProviderRequest,
  type TestConnectionResult,
  type ProviderInfo,
} from './api'

const loading = ref(false)
const saving = ref(false)
const testing = ref(false)
const error = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const testResult = ref<TestConnectionResult | null>(null)
const providers = ref<ProviderInfo[]>([])
const availableModels = ref<string[]>([])
const loadingModels = ref(false)
// Form state
const activeProvider = ref<AiProviderType>(AiProviderType.Fake)

// OpenAI fields
const openAiApiKey = ref('')
const openAiModel = ref('gpt-4o-mini')

// Azure OpenAI fields
const azureApiKey = ref('')
const azureEndpoint = ref('')
const azureDeploymentName = ref('')
const azureApiVersion = ref('2024-02-01')

// AWS Bedrock fields
const awsAccessKeyId = ref('')
const awsSecretAccessKey = ref('')
const awsRegion = ref('us-east-1')
const awsModelId = ref('')

// Ollama fields
const ollamaBaseUrl = ref('http://localhost:11434')
const ollamaModel = ref('llama3')

// Grok fields
const grokApiKey = ref('')
const grokModel = ref('grok-3-mini')

const providerLabel = computed(() => {
  const p = providers.value.find((p) => p.id === activeProvider.value)
  return p?.name ?? 'Unknown'
})

onMounted(async () => {
  await loadConfig()
  await loadProviders()
  await fetchModels()
})

async function loadProviders() {
  try {
    providers.value = await getProviders()
  } catch {
    // Non-critical - use defaults
    providers.value = [
      { id: AiProviderType.Fake, name: 'Fake (Development)', description: 'Deterministic fake provider for testing' },
      { id: AiProviderType.OpenAi, name: 'OpenAI', description: 'GPT-4o, GPT-4o-mini, and other OpenAI models' },
      { id: AiProviderType.AzureOpenAi, name: 'Azure OpenAI', description: 'OpenAI models hosted on Azure' },
      { id: AiProviderType.AwsBedrock, name: 'AWS Bedrock', description: 'Claude, Titan, and other models via AWS' },
      { id: AiProviderType.Ollama, name: 'Ollama', description: 'Local models via Ollama (fully offline)' },
      { id: AiProviderType.Grok, name: 'Grok (xAI)', description: 'Grok models via xAI API' },
    ]
  }
}

async function fetchModels() {
  loadingModels.value = true
  try {
    availableModels.value = await getAvailableModels()
  } catch {
    availableModels.value = []
  } finally {
    loadingModels.value = false
  }
}

async function loadConfig() {
  loading.value = true
  error.value = null
  try {
    const config = await getAiConfig()
    activeProvider.value = config.activeProvider

    // Populate fields from config (keys will be masked)
    if (config.openAi) {
      openAiApiKey.value = config.openAi.apiKey ?? ''
      openAiModel.value = config.openAi.model ?? 'gpt-4o-mini'
    }
    if (config.azureOpenAi) {
      azureApiKey.value = config.azureOpenAi.apiKey ?? ''
      azureEndpoint.value = config.azureOpenAi.endpoint ?? ''
      azureDeploymentName.value = config.azureOpenAi.deploymentName ?? ''
      azureApiVersion.value = config.azureOpenAi.apiVersion ?? '2024-02-01'
    }
    if (config.awsBedrock) {
      awsAccessKeyId.value = config.awsBedrock.accessKeyId ?? ''
      awsSecretAccessKey.value = config.awsBedrock.secretAccessKey ?? ''
      awsRegion.value = config.awsBedrock.region ?? 'us-east-1'
      awsModelId.value = config.awsBedrock.modelId ?? ''
    }
    if (config.ollama) {
      ollamaBaseUrl.value = config.ollama.baseUrl ?? 'http://localhost:11434'
      ollamaModel.value = config.ollama.model ?? 'llama3'
    }
    if (config.grok) {
      grokApiKey.value = config.grok.apiKey ?? ''
      grokModel.value = config.grok.model ?? 'grok-3-mini'
    }
  } catch (e: any) {
    error.value = e?.response?.data?.error ?? e?.message ?? 'Failed to load AI settings'
  } finally {
    loading.value = false
  }
}

function buildRequest(): UpdateAiProviderRequest {
  const request: UpdateAiProviderRequest = {
    activeProvider: activeProvider.value,
  }

  switch (activeProvider.value) {
    case AiProviderType.OpenAi:
      request.openAi = {
        apiKey: isKeyMasked(openAiApiKey.value) ? undefined : openAiApiKey.value || undefined,
        model: openAiModel.value,
      }
      break
    case AiProviderType.AzureOpenAi:
      request.azureOpenAi = {
        apiKey: isKeyMasked(azureApiKey.value) ? undefined : azureApiKey.value || undefined,
        endpoint: azureEndpoint.value || undefined,
        deploymentName: azureDeploymentName.value || undefined,
        apiVersion: azureApiVersion.value || undefined,
      }
      break
    case AiProviderType.AwsBedrock:
      request.awsBedrock = {
        accessKeyId: isKeyMasked(awsAccessKeyId.value) ? undefined : awsAccessKeyId.value || undefined,
        secretAccessKey: isKeyMasked(awsSecretAccessKey.value) ? undefined : awsSecretAccessKey.value || undefined,
        region: awsRegion.value || undefined,
        modelId: awsModelId.value || undefined,
      }
      break
    case AiProviderType.Ollama:
      request.ollama = {
        baseUrl: ollamaBaseUrl.value,
        model: ollamaModel.value,
      }
      break
    case AiProviderType.Grok:
      request.grok = {
        apiKey: isKeyMasked(grokApiKey.value) ? undefined : grokApiKey.value || undefined,
        model: grokModel.value,
      }
      break
  }

  return request
}

async function saveConfig() {
  saving.value = true
  error.value = null
  successMessage.value = null
  testResult.value = null

  try {
    const request = buildRequest()
    await updateAiConfig(request)
    successMessage.value = `AI provider updated to ${providerLabel.value}.`
    await loadConfig()
    await fetchModels()
  } catch (e: any) {
    error.value = e?.response?.data?.error ?? e?.message ?? 'Failed to save AI settings'
  } finally {
    saving.value = false
  }
}

async function testConnection() {
  testing.value = true
  error.value = null
  testResult.value = null
  successMessage.value = null

  try {
    const request = buildRequest()
    testResult.value = await testAiConnection(request)
  } catch (e: any) {
    testResult.value = {
      success: false,
      message: e?.response?.data?.message ?? e?.message ?? 'Connection test failed',
    }
  } finally {
    testing.value = false
  }
}

/** Returns true if the value looks like a masked key (all asterisks except last 4 chars). */
function isKeyMasked(value: string): boolean {
  if (!value || value.length < 5) return false
  const prefix = value.slice(0, -4)
  return prefix.split('').every((c) => c === '*')
}
</script>

<template>
  <div class="mx-auto max-w-2xl space-y-6">
    <!-- Page header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-bold text-white">AI Provider Settings</h1>
        <p class="mt-1 text-sm text-gray-400">
          Configure which AI provider powers rules lookup, narration, and the AI DM runtime.
          API keys are stored server-side only and never sent to the browser.
        </p>
      </div>
    </div>

    <!-- Loading state -->
    <div v-if="loading" class="text-gray-400">Loading configuration...</div>

    <!-- Error banner -->
    <div
      v-if="error"
      class="rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
      role="alert"
    >
      {{ error }}
    </div>

    <!-- Success banner -->
    <output
      v-if="successMessage"
      class="block rounded-lg border border-green-800 bg-green-950 px-4 py-3 text-sm text-green-300"
    >
      {{ successMessage }}
    </output>

    <!-- Test result banner -->
    <div
      v-if="testResult"
      :class="[
        'rounded-lg px-4 py-3 border text-sm',
        testResult.success
          ? 'bg-green-950 border-green-800 text-green-300'
          : 'bg-red-950 border-red-800 text-red-300',
      ]"
      role="status"
    >
      <p class="font-medium">{{ testResult.success ? '✓ Connection successful' : '✗ Connection failed' }}</p>
      <p v-if="testResult.message" class="mt-1">{{ testResult.message }}</p>
      <p v-if="testResult.model" class="mt-1">Model: {{ testResult.model }}</p>
    </div>

    <form v-if="!loading" @submit.prevent="saveConfig" class="space-y-6">
      <!-- Provider selector -->
      <div>
        <label for="provider-select" class="block text-sm font-medium text-gray-300 mb-1">
          Active AI Provider
        </label>
        <select
          id="provider-select"
          v-model.number="activeProvider"
          class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500 focus:border-aircane-500"
        >
          <option v-for="p in providers" :key="p.id" :value="p.id">
            {{ p.name }}
          </option>
        </select>
        <p class="text-sm text-gray-500 mt-1">
          {{ providers.find((p) => p.id === activeProvider)?.description }}
        </p>
      </div>

      <!-- OpenAI settings -->
      <fieldset
        v-if="activeProvider === AiProviderType.OpenAi"
        class="border border-surface-700/50 rounded-md p-4 space-y-4"
      >
        <legend class="text-sm font-medium text-gray-300 px-2">OpenAI Configuration</legend>

        <div>
          <label for="openai-key" class="block text-sm font-medium text-gray-300 mb-1">API Key</label>
          <input
            id="openai-key"
            v-model="openAiApiKey"
            type="password"
            placeholder="sk-..."
            autocomplete="off"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
          <p class="text-xs text-gray-500 mt-1">Stored server-side only. Leave unchanged to keep existing key.</p>
        </div>

        <div>
          <label for="openai-model" class="block text-sm font-medium text-gray-300 mb-1">Model</label>
          <select
            v-if="availableModels.length > 0"
            id="openai-model"
            v-model="openAiModel"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          >
            <option v-for="model in availableModels" :key="model" :value="model">
              {{ model }}
            </option>
          </select>
          <input
            v-else
            id="openai-model"
            v-model="openAiModel"
            type="text"
            placeholder="gpt-4o-mini"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
          <p v-if="loadingModels" class="text-xs text-gray-500 mt-1">Loading available models...</p>
        </div>
      </fieldset>

      <!-- Azure OpenAI settings -->
      <fieldset
        v-if="activeProvider === AiProviderType.AzureOpenAi"
        class="border border-surface-700/50 rounded-md p-4 space-y-4"
      >
        <legend class="text-sm font-medium text-gray-300 px-2">Azure OpenAI Configuration</legend>

        <div>
          <label for="azure-key" class="block text-sm font-medium text-gray-300 mb-1">API Key</label>
          <input
            id="azure-key"
            v-model="azureApiKey"
            type="password"
            placeholder="Azure API key"
            autocomplete="off"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="azure-endpoint" class="block text-sm font-medium text-gray-300 mb-1">Endpoint</label>
          <input
            id="azure-endpoint"
            v-model="azureEndpoint"
            type="url"
            placeholder="https://your-resource.openai.azure.com/"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="azure-deployment" class="block text-sm font-medium text-gray-300 mb-1">Deployment Name</label>
          <input
            id="azure-deployment"
            v-model="azureDeploymentName"
            type="text"
            placeholder="gpt-4o-mini"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="azure-version" class="block text-sm font-medium text-gray-300 mb-1">API Version</label>
          <input
            id="azure-version"
            v-model="azureApiVersion"
            type="text"
            placeholder="2024-02-01"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </fieldset>

      <!-- AWS Bedrock settings -->
      <fieldset
        v-if="activeProvider === AiProviderType.AwsBedrock"
        class="border border-surface-700/50 rounded-md p-4 space-y-4"
      >
        <legend class="text-sm font-medium text-gray-300 px-2">AWS Bedrock Configuration</legend>

        <div>
          <label for="aws-key-id" class="block text-sm font-medium text-gray-300 mb-1">Access Key ID</label>
          <input
            id="aws-key-id"
            v-model="awsAccessKeyId"
            type="password"
            placeholder="AKIA..."
            autocomplete="off"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="aws-secret" class="block text-sm font-medium text-gray-300 mb-1">Secret Access Key</label>
          <input
            id="aws-secret"
            v-model="awsSecretAccessKey"
            type="password"
            placeholder="Secret access key"
            autocomplete="off"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="aws-region" class="block text-sm font-medium text-gray-300 mb-1">Region</label>
          <input
            id="aws-region"
            v-model="awsRegion"
            type="text"
            placeholder="us-east-1"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>

        <div>
          <label for="aws-model" class="block text-sm font-medium text-gray-300 mb-1">Model ID</label>
          <input
            id="aws-model"
            v-model="awsModelId"
            type="text"
            placeholder="anthropic.claude-3-sonnet-20240229-v1:0"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </fieldset>

      <!-- Ollama settings -->
      <fieldset
        v-if="activeProvider === AiProviderType.Ollama"
        class="border border-surface-700/50 rounded-md p-4 space-y-4"
      >
        <legend class="text-sm font-medium text-gray-300 px-2">Ollama Configuration</legend>

        <div>
          <label for="ollama-url" class="block text-sm font-medium text-gray-300 mb-1">Base URL</label>
          <input
            id="ollama-url"
            v-model="ollamaBaseUrl"
            type="url"
            placeholder="http://localhost:11434"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
          <p class="text-xs text-gray-500 mt-1">No API key required. Ollama runs fully offline.</p>
        </div>

        <div>
          <label for="ollama-model" class="block text-sm font-medium text-gray-300 mb-1">Model</label>
          <input
            id="ollama-model"
            v-model="ollamaModel"
            type="text"
            placeholder="llama3"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </fieldset>

      <!-- Grok settings -->
      <fieldset
        v-if="activeProvider === AiProviderType.Grok"
        class="border border-surface-700/50 rounded-md p-4 space-y-4"
      >
        <legend class="text-sm font-medium text-gray-300 px-2">Grok (xAI) Configuration</legend>

        <div>
          <label for="grok-key" class="block text-sm font-medium text-gray-300 mb-1">API Key</label>
          <input
            id="grok-key"
            v-model="grokApiKey"
            type="password"
            placeholder="xai-..."
            autocomplete="off"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
          <p class="text-xs text-gray-500 mt-1">Stored server-side only. Leave unchanged to keep existing key.</p>
        </div>

        <div>
          <label for="grok-model" class="block text-sm font-medium text-gray-300 mb-1">Model</label>
          <input
            id="grok-model"
            v-model="grokModel"
            type="text"
            placeholder="grok-3-mini"
            class="w-full rounded-md border border-gray-700 bg-surface-850 px-3 py-2 text-gray-100 focus:ring-2 focus:ring-aircane-500"
          />
        </div>
      </fieldset>

      <!-- Fake provider info -->
      <div
        v-if="activeProvider === AiProviderType.Fake"
        class="rounded-lg border border-yellow-800 bg-yellow-950/40 px-4 py-3 text-sm text-yellow-300"
      >
        <p class="font-medium">Development Mode</p>
        <p class="mt-1 text-yellow-400">
          The fake provider returns deterministic responses for testing. No external API calls are made.
        </p>
      </div>

      <!-- Action buttons -->
      <div class="flex gap-3 pt-2">
        <button
          type="submit"
          :disabled="saving"
          class="rounded-lg bg-aircane-600 px-4 py-2 text-sm font-semibold text-white shadow hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {{ saving ? 'Saving...' : 'Save Configuration' }}
        </button>

        <button
          type="button"
          :disabled="testing"
          class="rounded-lg border border-gray-600 bg-surface-850 px-4 py-2 text-sm font-semibold text-gray-300 shadow hover:border-gray-500 hover:text-gray-100 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50 disabled:cursor-not-allowed"
          @click="testConnection"
        >
          {{ testing ? 'Testing...' : 'Test Connection' }}
        </button>
      </div>
    </form>
  </div>
</template>

import apiClient from '@/shared/api/client'

export enum AiProviderType {
  Fake = 0,
  OpenAi = 1,
  AzureOpenAi = 2,
  AwsBedrock = 3,
  Ollama = 4,
  Grok = 5,
}

export interface OpenAiSettings {
  apiKey?: string | null
  model: string
}

export interface AzureOpenAiSettings {
  apiKey?: string | null
  endpoint?: string | null
  deploymentName?: string | null
  apiVersion?: string | null
}

export interface AwsBedrockSettings {
  accessKeyId?: string | null
  secretAccessKey?: string | null
  region?: string | null
  modelId?: string | null
}

export interface OllamaSettings {
  baseUrl: string
  model: string
}

export interface GrokSettings {
  apiKey?: string | null
  model: string
}

export interface AiProviderConfig {
  activeProvider: AiProviderType
  openAi?: OpenAiSettings | null
  azureOpenAi?: AzureOpenAiSettings | null
  awsBedrock?: AwsBedrockSettings | null
  ollama?: OllamaSettings | null
  grok?: GrokSettings | null
}

export interface UpdateAiProviderRequest {
  activeProvider: AiProviderType
  openAi?: OpenAiSettings | null
  azureOpenAi?: AzureOpenAiSettings | null
  awsBedrock?: AwsBedrockSettings | null
  ollama?: OllamaSettings | null
  grok?: GrokSettings | null
}

export interface TestConnectionResult {
  success: boolean
  message?: string | null
  providerName?: string | null
  model?: string | null
}

export interface ProviderInfo {
  id: AiProviderType
  name: string
  description: string
}

export async function getAiConfig(): Promise<AiProviderConfig> {
  const response = await apiClient.get<AiProviderConfig>('/api/ai/settings')
  return response.data
}

export async function updateAiConfig(request: UpdateAiProviderRequest): Promise<AiProviderConfig> {
  const response = await apiClient.put<AiProviderConfig>('/api/ai/settings', request)
  return response.data
}

export async function testAiConnection(request: UpdateAiProviderRequest): Promise<TestConnectionResult> {
  const response = await apiClient.post<TestConnectionResult>('/api/ai/settings/test-connection', request)
  return response.data
}

export async function getProviders(): Promise<ProviderInfo[]> {
  const response = await apiClient.get<ProviderInfo[]>('/api/ai/settings/providers')
  return response.data
}

export interface ModelsResponse {
  models: string[]
}

export async function getAvailableModels(): Promise<string[]> {
  const response = await apiClient.get<ModelsResponse>('/api/ai/settings/models')
  return response.data.models
}

// --- Rules Question API ---

export interface RulesQuestionRequest {
  question: string
  gameSystem?: string | null
  ruleset?: string | null
  campaignId?: string | null
}

export interface RulesQuestionCitation {
  sourceTitle: string
  pageNumber: number | null
  sectionTitle: string | null
  chunkId: string
}

export interface RulesQuestionResponse {
  answer: string
  citations: RulesQuestionCitation[]
  hasSourceSupport: boolean
}

export async function askRulesQuestion(request: RulesQuestionRequest): Promise<RulesQuestionResponse> {
  const response = await apiClient.post<RulesQuestionResponse>('/api/ai/rules-question', request, {
    timeout: 120_000, // RAG retrieval + AI generation can take a while with large libraries
  })
  return response.data
}

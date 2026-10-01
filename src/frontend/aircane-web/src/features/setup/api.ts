import apiClient from '@/shared/api/client'

/**
 * First-launch / configuration status returned by GET /api/setup/status.
 * The endpoint is anonymous (called before any session exists).
 */
export interface SetupStatus {
  isFirstLaunch: boolean
  aiProviderConfigured: boolean
  activeAiProvider: string
  activeEmbeddingProvider: string
  ollamaReachable: boolean
}

/** Fetches first-launch / AI-configuration status for the setup wizard. */
export async function getSetupStatus(): Promise<SetupStatus> {
  const response = await apiClient.get<SetupStatus>('/api/setup/status', {
    // Keep this snappy — it's polled every few seconds during Ollama setup.
    timeout: 5_000,
  })
  return response.data
}

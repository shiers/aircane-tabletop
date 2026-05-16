import apiClient from './client'

export interface HealthResponse {
  status: string
}

/**
 * Calls GET /health and returns the backend health status string.
 * Uses a short dedicated timeout so health checks aren't affected by
 * long-running requests on the same client.
 * Throws if the request fails or the response is not 2xx.
 */
export async function checkHealth(): Promise<HealthResponse> {
  const response = await apiClient.get<HealthResponse>('/health', {
    timeout: 5_000, // Short timeout — health endpoint should respond fast
  })
  return response.data
}

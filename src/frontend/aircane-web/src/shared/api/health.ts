import apiClient from './client'

export interface HealthResponse {
  status: string
}

/**
 * Calls GET /health and returns the backend health status string.
 * Throws if the request fails or the response is not 2xx.
 */
export async function checkHealth(): Promise<HealthResponse> {
  const response = await apiClient.get<HealthResponse>('/health')
  return response.data
}

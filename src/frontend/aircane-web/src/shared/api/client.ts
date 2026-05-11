import axios from 'axios'

/**
 * Shared axios instance for all API calls.
 * Base URL is read from the VITE_API_BASE_URL environment variable,
 * falling back to the Vite dev-server proxy path so the proxy handles
 * routing in development when the env var is not set.
 */
const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 10_000,
})

// Request interceptor — attach auth token when available
apiClient.interceptors.request.use((config) => {
  const token = sessionStorage.getItem('participant_token')
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Response interceptor — surface errors consistently
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // Errors are re-thrown so callers can handle them
    return Promise.reject(error)
  },
)

export default apiClient

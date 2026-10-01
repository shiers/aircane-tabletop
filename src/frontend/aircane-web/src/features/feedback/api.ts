import apiClient from '@/shared/api/client'
import type { DiagnosticContext } from './composables/useFeedbackDiagnostics'

export interface FeedbackPayload {
  summary: string
  description: string
  stepsToReproduce?: string | null
  expectedBehaviour?: string | null
  diagnosticContext: DiagnosticContext
  recentEvents: string[]
  consoleErrors: string[]
}

export interface FeedbackResponse {
  success: boolean
  issueUrl: string
  issueNumber: number
}

/**
 * Submits a bug report. The backend proxies it to GitHub Issues; the browser never calls GitHub
 * directly and never sees the GitHub token.
 */
export async function submitFeedback(payload: FeedbackPayload): Promise<FeedbackResponse> {
  const response = await apiClient.post<FeedbackResponse>('/api/feedback', payload)
  return response.data
}

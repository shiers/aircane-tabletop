import { describe, it, expect, vi, beforeEach } from 'vitest'

// ── Mocks ──────────────────────────────────────────────────────────────────────
const mockRoute = { value: { fullPath: '/sessions/abc123/host' } }
vi.mock('vue-router', () => ({
  useRouter: () => ({ currentRoute: mockRoute }),
}))

const mockSession = { id: 'abc123', campaignId: 'def456' }
vi.mock('@/features/sessions/store', () => ({
  useSessionStore: () => ({ currentSession: mockSession }),
}))

vi.mock('@/features/ai/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/ai/api')>()
  return {
    ...actual,
    getAiConfig: vi.fn(),
  }
})

import { useFeedbackDiagnostics } from '../composables/useFeedbackDiagnostics'
import * as aiApi from '@/features/ai/api'
import { clearEventBuffer, pushEvent } from '../composables/useFeedbackEventBuffer'

const mockedGetAiConfig = vi.mocked(aiApi.getAiConfig)

describe('useFeedbackDiagnostics', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    clearEventBuffer()
    mockedGetAiConfig.mockResolvedValue({
      activeProvider: aiApi.AiProviderType.Ollama,
      ollama: { baseUrl: 'http://localhost:11434', model: 'llama3.2' },
    })
  })

  it('captures the current route and session context', async () => {
    const { capture } = useFeedbackDiagnostics()
    const ctx = await capture()

    expect(ctx.activeRoute).toBe('/sessions/abc123/host')
    expect(ctx.sessionId).toBe('abc123')
    expect(ctx.campaignId).toBe('def456')
  })

  it('maps the active AI provider and model from getAiConfig', async () => {
    const { capture } = useFeedbackDiagnostics()
    const ctx = await capture()

    expect(ctx.aiProvider).toBe('Ollama')
    expect(ctx.aiModel).toBe('llama3.2')
    // Embedding fields are not exposed to the frontend today.
    expect(ctx.embeddingProvider).toBeNull()
    expect(ctx.embeddingModel).toBeNull()
    expect(ctx.userRole).toBeNull()
  })

  it('still resolves when getAiConfig fails (Player browser)', async () => {
    mockedGetAiConfig.mockRejectedValue(new Error('403'))
    const { capture } = useFeedbackDiagnostics()
    const ctx = await capture()

    expect(ctx.aiProvider).toBeNull()
    expect(ctx.aiModel).toBeNull()
    // Route/session capture is unaffected.
    expect(ctx.sessionId).toBe('abc123')
  })

  it('reports counts of captured events and errors', () => {
    pushEvent({ actor: 'Player', eventType: 'RollRecorded', keyField: '1d20 = 10' })
    const { counts } = useFeedbackDiagnostics()
    expect(counts().recentEvents).toBe(1)
  })
})

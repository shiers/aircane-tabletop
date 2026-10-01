/**
 * Captures non-sensitive diagnostic context for an in-app bug report: route, session, platform,
 * and the active AI provider/model (best-effort). It reads the router and the session store, and
 * fetches the AI config once (swallowing errors so a Player browser without access still submits).
 *
 * SECURITY: this captures no campaign content, character data, narration text, PDF content, or
 * API keys — only identifiers (session/campaign IDs) and provider/model names.
 *
 * NOTED STORE MISMATCHES (vs. the task's intended field names):
 *  - The session store exposes `currentSession` (a SessionDto with `.id`/`.campaignId`), not
 *    `currentSessionId`/`currentCampaignId`. We read `currentSession?.id`/`.campaignId`.
 *  - There is no global user-role store, so `userRole` is sent as null.
 *  - There is no AI settings Pinia store exposing `activeProvider`/`activeModel`; AI config comes
 *    from a best-effort `getAiConfig()` call. Embedding provider/model are not exposed to the
 *    frontend today, so `embeddingProvider`/`embeddingModel` are sent as null.
 */
import { useRouter } from 'vue-router'
import { useSessionStore } from '@/features/sessions/store'
import { getAiConfig, AiProviderType, type AiProviderConfig } from '@/features/ai/api'
import { snapshot } from './useFeedbackEventBuffer'
import { getConsoleErrors } from '../errorBuffer'

export interface DiagnosticContext {
  platform: string | null
  userAgent: string | null
  appVersion: string | null
  isTauri: boolean
  tauriVersion: string | null
  activeRoute: string | null
  sessionId: string | null
  campaignId: string | null
  userRole: string | null
  aiProvider: string | null
  aiModel: string | null
  embeddingProvider: string | null
  embeddingModel: string | null
}

/** Counts of auto-captured data, shown in the modal's diagnostic section. */
export interface DiagnosticCounts {
  recentEvents: number
  consoleErrors: number
}

function providerDisplayName(type: AiProviderType): string {
  switch (type) {
    case AiProviderType.OpenAi:
      return 'OpenAI'
    case AiProviderType.AzureOpenAi:
      return 'Azure OpenAI'
    case AiProviderType.AwsBedrock:
      return 'AWS Bedrock'
    case AiProviderType.Ollama:
      return 'Ollama'
    case AiProviderType.Grok:
      return 'Grok'
    case AiProviderType.Fake:
    default:
      return 'None'
  }
}

function providerModel(config: AiProviderConfig): string | null {
  switch (config.activeProvider) {
    case AiProviderType.OpenAi:
      return config.openAi?.model ?? null
    case AiProviderType.AzureOpenAi:
      return config.azureOpenAi?.deploymentName ?? null
    case AiProviderType.AwsBedrock:
      return config.awsBedrock?.modelId ?? null
    case AiProviderType.Ollama:
      return config.ollama?.model ?? null
    case AiProviderType.Grok:
      return config.grok?.model ?? null
    default:
      return null
  }
}

function tauriVersion(): string | null {
  const t = window.__TAURI__ as { version?: string } | undefined
  return t?.version ?? null
}

export function useFeedbackDiagnostics() {
  const router = useRouter()
  const sessionStore = useSessionStore()

  async function capture(): Promise<DiagnosticContext> {
    let aiProvider: string | null = null
    let aiModel: string | null = null

    try {
      const config = await getAiConfig()
      aiProvider = providerDisplayName(config.activeProvider)
      aiModel = providerModel(config)
    } catch {
      // Best-effort: Player browsers (or an unreachable backend) simply report null AI info.
    }

    return {
      platform: typeof navigator !== 'undefined' ? navigator.platform : null,
      userAgent: typeof navigator !== 'undefined' ? navigator.userAgent : null,
      appVersion: import.meta.env.VITE_APP_VERSION ?? null,
      isTauri: !!window.__TAURI__,
      tauriVersion: tauriVersion(),
      activeRoute: router.currentRoute.value.fullPath,
      sessionId: sessionStore.currentSession?.id ?? null,
      campaignId: sessionStore.currentSession?.campaignId ?? null,
      userRole: null,
      aiProvider,
      aiModel,
      embeddingProvider: null,
      embeddingModel: null,
    }
  }

  function counts(): DiagnosticCounts {
    return {
      recentEvents: snapshot().length,
      consoleErrors: getConsoleErrors().length,
    }
  }

  return { capture, counts }
}

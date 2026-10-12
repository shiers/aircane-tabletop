import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import { generateAdventure, type GenerateAdventureRequest } from './api'

const HUB_URL = '/hubs/library'
const RECONNECT_DELAY_MS = 5_000

export type GenerationStatus = 'idle' | 'generating' | 'completed' | 'failed'

interface ProgressEvent {
  jobId: string
  stage: string
  percentComplete: number
  message: string
}
interface CompletedEvent {
  jobId: string
  adventureId: string
  adventureTitle: string
}
interface FailedEvent {
  jobId: string
  error: string
}

function errorText(e: unknown, fallback: string): string {
  const data = (e as { response?: { data?: { errors?: Record<string, string[]>; detail?: string } } })?.response?.data
  if (data?.errors) return (Object.values(data.errors).flat() as string[]).join(' ')
  return data?.detail ?? (e instanceof Error ? e.message : fallback)
}

/**
 * Tracks the (single) in-flight adventure generation job. Lives at app level so progress survives
 * navigation: the job runs on the server, and progress / completion arrive over SignalR.
 */
export const useAdventureGenerationStore = defineStore('adventureGeneration', () => {
  const status = ref<GenerationStatus>('idle')
  const jobId = ref<string | null>(null)
  const stage = ref('')
  const percentComplete = ref(0)
  const message = ref('')
  const adventureId = ref<string | null>(null)
  const adventureTitle = ref('')
  const error = ref<string | null>(null)
  /** Whether the toast is showing. Dismissing hides it without forgetting the result. */
  const toastVisible = ref(false)

  const isGenerating = computed(() => status.value === 'generating')

  let connection: signalR.HubConnection | null = null
  let stopped = false
  let lastRequest: GenerateAdventureRequest | null = null
  let lastCampaignId: string | null = null

  // ── SignalR ────────────────────────────────────────────────────────────────

  /**
   * Whether this event is about the job we care about. While a job is in flight only its own events
   * count. Otherwise (nothing tracked: after a reload / hot-reload, or from another tab) the event is
   * adopted, so a finished adventure is never silently missed.
   */
  function accepts(event: { jobId: string }): boolean {
    if (status.value === 'generating' && jobId.value !== null) return event.jobId === jobId.value
    if (jobId.value === event.jobId) return true
    adopt(event.jobId)
    return true
  }

  function adopt(id: string): void {
    jobId.value = id
    status.value = 'generating'
    percentComplete.value = 0
    stage.value = ''
    message.value = 'Generating...'
    error.value = null
  }

  function onProgress(e: ProgressEvent): void {
    if (!accepts(e) || status.value !== 'generating') return
    toastVisible.value = true
    // Never let the bar move backwards if events arrive out of order.
    percentComplete.value = Math.max(percentComplete.value, e.percentComplete)
    stage.value = e.stage
    message.value = e.message
  }

  function onCompleted(e: CompletedEvent): void {
    if (!accepts(e)) return
    status.value = 'completed'
    percentComplete.value = 100
    message.value = 'Adventure ready!'
    adventureId.value = e.adventureId
    adventureTitle.value = e.adventureTitle
    toastVisible.value = true
  }

  function onFailed(e: FailedEvent): void {
    if (!accepts(e)) return
    status.value = 'failed'
    error.value = e.error
    toastVisible.value = true
  }

  async function join(conn: signalR.HubConnection): Promise<void> {
    await conn.invoke('JoinAdventureGenerationGroup')
  }

  async function ensureConnected(): Promise<void> {
    if (connection) return

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    conn.on('AdventureGenerationProgress', onProgress)
    conn.on('AdventureGenerationCompleted', onCompleted)
    conn.on('AdventureGenerationFailed', onFailed)
    // Group membership is per connection, so a reconnect has to join again.
    conn.onreconnected(async () => {
      try {
        await join(conn)
      } catch {
        // retried on the next reconnect
      }
    })

    // Reconnect attempts exhausted (e.g. the backend restarted): forget the dead connection so the
    // next start() or retry builds a fresh one, and keep trying in the background.
    conn.onclose(() => {
      if (connection === conn) connection = null
      scheduleReconnect()
    })

    await conn.start()
    await join(conn)
    connection = conn
  }

  let reconnectTimer: ReturnType<typeof setTimeout> | null = null

  function scheduleReconnect(): void {
    if (reconnectTimer || stopped) return
    reconnectTimer = setTimeout(async () => {
      reconnectTimer = null
      try {
        await ensureConnected()
      } catch {
        connection = null
        scheduleReconnect()
      }
    }, RECONNECT_DELAY_MS)
  }

  /**
   * Start listening for generation events for the lifetime of the app. Called once from App.vue, so
   * the listeners belong to the app and not to any page: navigating around never affects them.
   * Failure is not fatal (the backend may not be up yet); it keeps retrying, and start() also retries.
   */
  async function initialize(): Promise<void> {
    stopped = false
    try {
      await ensureConnected()
    } catch {
      connection = null
      scheduleReconnect()
    }
  }

  // ── Actions ────────────────────────────────────────────────────────────────

  /**
   * Queue a generation job. Listens for progress *before* posting, so no event can be missed.
   * Throws (leaving the store idle) if the live connection or the request fails.
   */
  async function start(request: GenerateAdventureRequest, campaignId?: string | null): Promise<void> {
    if (isGenerating.value) return
    lastRequest = request
    lastCampaignId = campaignId ?? null
    error.value = null

    try {
      await ensureConnected()
    } catch {
      connection = null
      throw new Error('Could not connect for live progress. Is the backend running?')
    }

    try {
      const response = await generateAdventure(request, campaignId)
      jobId.value = response.jobId
    } catch (e) {
      throw new Error(errorText(e, 'Failed to start adventure generation.'))
    }

    status.value = 'generating'
    stage.value = ''
    percentComplete.value = 0
    message.value = 'Starting...'
    adventureId.value = null
    adventureTitle.value = ''
    toastVisible.value = true
  }

  /** Run the last request again after a failure. */
  async function retry(): Promise<void> {
    if (!lastRequest) return
    try {
      await start(lastRequest, lastCampaignId)
    } catch (e) {
      status.value = 'failed'
      error.value = e instanceof Error ? e.message : 'Failed to start adventure generation.'
      toastVisible.value = true
    }
  }

  /** Hide the toast. A running job keeps running and the form keeps showing progress. */
  function dismissToast(): void {
    toastVisible.value = false
    if (status.value === 'completed' || status.value === 'failed') {
      status.value = 'idle'
    }
  }

  async function disconnect(): Promise<void> {
    stopped = true
    if (reconnectTimer) clearTimeout(reconnectTimer)
    reconnectTimer = null
    const conn = connection
    connection = null
    if (!conn) return
    try {
      await conn.stop()
    } catch {
      // ignore
    }
  }

  return {
    status,
    jobId,
    stage,
    percentComplete,
    message,
    adventureId,
    adventureTitle,
    error,
    toastVisible,
    isGenerating,
    initialize,
    start,
    retry,
    dismissToast,
    disconnect,
  }
})

import { ref, onUnmounted } from 'vue'
import { getSetupStatus } from '../api'

/** Live Ollama detection state for the setup wizard's status indicator. */
export type OllamaStatus = 'checking' | 'running' | 'not-running'

const POLL_INTERVAL_MS = 3_000

/**
 * Polls GET /api/setup/status every 3 seconds and exposes whether Ollama is
 * reachable. Used by the Ollama setup step to show a live indicator without a
 * manual "check" button. The poller MUST be stopped when the wizard closes to
 * avoid background fetches after setup — call `stop()`, or rely on the automatic
 * `onUnmounted` teardown when used inside a component.
 */
export function useOllamaPoller() {
  const status = ref<OllamaStatus>('checking')
  let timer: ReturnType<typeof setInterval> | null = null
  let inFlight = false

  async function pollOnce(): Promise<void> {
    // Guard against overlapping polls if a request is slow.
    if (inFlight) return
    inFlight = true
    try {
      const result = await getSetupStatus()
      status.value = result.ollamaReachable ? 'running' : 'not-running'
    } catch {
      status.value = 'not-running'
    } finally {
      inFlight = false
    }
  }

  function start(): void {
    if (timer !== null) return // already polling
    status.value = 'checking'
    // Kick off an immediate check, then poll on the interval.
    void pollOnce()
    timer = setInterval(() => {
      void pollOnce()
    }, POLL_INTERVAL_MS)
  }

  function stop(): void {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  // Safety net: stop polling if the host component unmounts without calling stop().
  onUnmounted(stop)

  return { status, start, stop }
}

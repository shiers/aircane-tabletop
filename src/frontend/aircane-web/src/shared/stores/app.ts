import { defineStore } from 'pinia'
import { ref } from 'vue'
import { checkHealth } from '@/shared/api/health'

export type HealthStatus = 'unknown' | 'healthy' | 'unreachable'

/** How often to re-check backend health (ms). */
const HEALTH_POLL_INTERVAL = 60_000

export const useAppStore = defineStore('app', () => {
  const healthStatus = ref<HealthStatus>('unknown')
  const healthMessage = ref<string>('')
  let pollTimer: ReturnType<typeof setInterval> | null = null

  async function fetchHealth(): Promise<void> {
    try {
      const data = await checkHealth()
      healthStatus.value = (data.status === 'healthy' || data.status === 'degraded') ? 'healthy' : 'unreachable'
      healthMessage.value = data.status
    } catch {
      healthStatus.value = 'unreachable'
      healthMessage.value = 'unreachable'
    }
  }

  /** Start periodic health polling. Safe to call multiple times. */
  function startHealthPolling(): void {
    if (pollTimer) return
    fetchHealth()
    pollTimer = setInterval(fetchHealth, HEALTH_POLL_INTERVAL)
  }

  /** Stop periodic health polling. */
  function stopHealthPolling(): void {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  }

  return {
    healthStatus,
    healthMessage,
    fetchHealth,
    startHealthPolling,
    stopHealthPolling,
  }
})

import { defineStore } from 'pinia'
import { ref } from 'vue'
import { checkHealth } from '@/shared/api/health'

export type HealthStatus = 'unknown' | 'healthy' | 'unreachable'

export const useAppStore = defineStore('app', () => {
  const healthStatus = ref<HealthStatus>('unknown')
  const healthMessage = ref<string>('')

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

  return {
    healthStatus,
    healthMessage,
    fetchHealth,
  }
})

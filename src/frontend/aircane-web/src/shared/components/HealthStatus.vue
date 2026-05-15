<script setup lang="ts">
import { onMounted } from 'vue'
import { useAppStore } from '@/shared/stores/app'

const appStore = useAppStore()

onMounted(async () => {
  await appStore.fetchHealth()
})
</script>

<template>
  <div class="inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-xs font-medium">
    <!-- Status indicator dot -->
    <span
      class="h-2 w-2 rounded-full"
      :class="{
        'bg-green-500 shadow-sm shadow-green-500/50': appStore.healthStatus === 'healthy',
        'bg-red-500 shadow-sm shadow-red-500/50': appStore.healthStatus === 'unreachable',
        'bg-gray-400 animate-pulse': appStore.healthStatus === 'unknown',
      }"
      aria-hidden="true"
    />

    <!-- Status label -->
    <span
      :class="{
        'text-green-400': appStore.healthStatus === 'healthy',
        'text-red-400': appStore.healthStatus === 'unreachable',
        'text-gray-500': appStore.healthStatus === 'unknown',
      }"
    >
      <template v-if="appStore.healthStatus === 'healthy'">Backend: healthy</template>
      <template v-else-if="appStore.healthStatus === 'unreachable'">Backend: unreachable</template>
      <template v-else>Backend: checking…</template>
    </span>
  </div>
</template>

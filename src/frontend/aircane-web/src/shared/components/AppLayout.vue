<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import HealthStatus from './HealthStatus.vue'
import AppSidebar from './AppSidebar.vue'

const route = useRoute()
const sidebarCollapsed = ref(false)

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}
</script>

<template>
  <div class="flex h-screen overflow-hidden bg-surface-950">
    <!-- Sidebar -->
    <AppSidebar :collapsed="sidebarCollapsed" @toggle="toggleSidebar" />

    <!-- Main content area -->
    <div class="flex flex-1 flex-col overflow-hidden">
      <!-- Top bar -->
      <header class="flex h-14 shrink-0 items-center justify-between border-b border-surface-800/60 bg-surface-900/50 px-6 backdrop-blur-sm">
        <div class="flex items-center gap-3">
          <!-- Mobile menu toggle -->
          <button
            class="rounded-md p-1.5 text-gray-400 hover:bg-surface-800 hover:text-white lg:hidden"
            aria-label="Toggle sidebar"
            @click="toggleSidebar"
          >
            <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>
        </div>

        <div class="flex items-center gap-4">
          <HealthStatus />
        </div>
      </header>

      <!-- Page content -->
      <main class="flex-1 overflow-y-auto p-6">
        <slot />
      </main>
    </div>
  </div>
</template>

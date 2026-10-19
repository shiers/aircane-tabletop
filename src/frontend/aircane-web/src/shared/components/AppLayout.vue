<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import HealthStatus from './HealthStatus.vue'
import AppSidebar from './AppSidebar.vue'
import FeedbackModal from '@/features/feedback/FeedbackModal.vue'
import { useFeedbackModal } from '@/features/feedback/useFeedbackModal'

const route = useRoute()

/** Below Tailwind's `lg` breakpoint the sidebar is a drawer behind the hamburger button. */
const NARROW_QUERY = '(max-width: 1023px)'

function isNarrowScreen(): boolean {
  return typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(NARROW_QUERY).matches
}

// On a phone the sidebar starts closed so the page gets the full width.
const sidebarCollapsed = ref(isNarrowScreen())

// Choosing a page on a narrow screen closes the drawer.
watch(
  () => route.fullPath,
  () => {
    if (isNarrowScreen()) sidebarCollapsed.value = true
  },
)

/**
 * Routes that drop their full-page art and must show a solid #0a0a1a fill.
 * Applied at the shell <main> level so the plain background covers the entire
 * content viewport (including the .page-container gutters and the top gap),
 * not just the routed view root. (`sessions` renders JoinSessionView too.)
 */
const plainBackgroundRoutes = new Set([
  'rules-lookup',
  'ai-settings',
  'adventure-generate',
  'join-session',
  'about',
  'sessions',
])

const isPlainBackground = computed(
  () => typeof route.name === 'string' && plainBackgroundRoutes.has(route.name),
)

const { isOpen: feedbackOpen, openFeedback, closeFeedback } = useFeedbackModal()

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}
</script>

<template>
  <div class="flex h-screen overflow-hidden bg-surface-950">
    <!-- Backdrop behind the open drawer (narrow screens only); tapping it closes the drawer -->
    <div
      v-if="!sidebarCollapsed"
      class="fixed inset-0 z-30 bg-black/60 lg:hidden"
      data-testid="sidebar-backdrop"
      aria-hidden="true"
      @click="toggleSidebar"
    />

    <!-- Sidebar -->
    <AppSidebar :collapsed="sidebarCollapsed" @toggle="toggleSidebar" />

    <!-- Main content area -->
    <div class="flex flex-1 flex-col overflow-hidden">
      <!-- Top bar -->
      <header
        class="flex h-14 shrink-0 items-center justify-between border-b border-surface-800/60 bg-surface-900/50 px-4 backdrop-blur-sm sm:px-6"
        v-bg-asset="{ url: '/assets/ui/topbar-background.png', fallback: '#0a0a1a' }"
      >
        <div class="flex items-center gap-3">
          <!-- Mobile menu toggle -->
          <button
            class="rounded-md p-1.5 text-gray-400 hover:bg-surface-800 hover:text-white lg:hidden"
            aria-label="Toggle sidebar"
            :aria-expanded="!sidebarCollapsed"
            @click="toggleSidebar"
          >
            <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>
        </div>

        <div class="flex items-center gap-4">
          <button
            class="flex items-center gap-1.5 rounded-md border border-surface-700 px-2 py-1 text-sm text-gray-300 hover:bg-surface-800 hover:text-white"
            title="Report a bug"
            aria-label="Report a bug"
            @click="openFeedback"
          >
            <span aria-hidden="true">🐛</span>
            <span class="hidden sm:inline">Report a bug</span>
          </button>
          <HealthStatus />
        </div>
      </header>

      <!-- Page content -->
      <main class="flex-1 overflow-y-auto pt-6" :class="{ 'page-plain': isPlainBackground }">
        <!-- Shared, centred content column (max-width 1200px). Views must not
             re-impose their own max-width / horizontal padding. -->
        <div class="page-container">
          <slot />
        </div>
      </main>
    </div>

    <!-- In-app bug report modal (opened from the nav button or the Tauri tray). -->
    <FeedbackModal v-if="feedbackOpen" @close="closeFeedback" />
  </div>
</template>

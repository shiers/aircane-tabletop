<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

defineProps<{
  collapsed: boolean
}>()

defineEmits<{
  toggle: []
}>()

const route = useRoute()

const navItems = [
  { label: 'Dashboard', to: '/', icon: 'dashboard' },
  { label: 'Campaigns', to: '/campaigns', icon: 'campaigns' },
  { label: 'Library', to: '/library', icon: 'library' },
  { label: 'Characters', to: '/characters', icon: 'characters' },
  { label: 'Rules Lookup', to: '/rules-lookup', icon: 'rules' },
  { label: 'AI DM', to: '/settings/ai', icon: 'ai' },
  { label: 'Adventure Forge', to: '/adventures/generate', icon: 'adventure' },
  { label: 'Dice Roller', to: '/sessions', icon: 'dice' },
]

function isActive(to: string): boolean {
  if (to === '/') return route.path === '/'
  return route.path.startsWith(to)
}
</script>

<template>
  <aside
    class="flex h-full flex-col border-r border-surface-800/60 bg-surface-900 transition-all duration-300"
    :class="collapsed ? 'w-16' : 'w-60'"
  >
    <!-- Logo area -->
    <div class="flex h-14 shrink-0 items-center gap-3 border-b border-surface-800/60 px-4">
      <div class="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-aircane-600 shadow-lg shadow-aircane-600/30">
        <svg class="h-4 w-4 text-white" fill="currentColor" viewBox="0 0 20 20">
          <path d="M10 2L3 7v6l7 5 7-5V7l-7-5zM10 4.2L15 7.5v5L10 15.8 5 12.5v-5L10 4.2z" />
        </svg>
      </div>
      <span
        v-if="!collapsed"
        class="text-lg font-bold tracking-tight text-white"
      >
        Aircane
      </span>
    </div>

    <!-- Navigation -->
    <nav class="flex-1 space-y-1 overflow-y-auto px-2 py-4" aria-label="Main navigation">
      <RouterLink
        v-for="item in navItems"
        :key="item.to"
        :to="item.to"
        class="group flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-all duration-150"
        :class="[
          isActive(item.to)
            ? 'bg-aircane-600/15 text-aircane-300 shadow-sm'
            : 'text-gray-400 hover:bg-surface-800 hover:text-gray-200',
        ]"
        :aria-current="isActive(item.to) ? 'page' : undefined"
      >
        <!-- Icons -->
        <span class="flex h-5 w-5 shrink-0 items-center justify-center">
          <!-- Dashboard -->
          <svg v-if="item.icon === 'dashboard'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
          </svg>
          <!-- Campaigns -->
          <svg v-else-if="item.icon === 'campaigns'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <!-- Library -->
          <svg v-else-if="item.icon === 'library'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
          </svg>
          <!-- Characters -->
          <svg v-else-if="item.icon === 'characters'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
          </svg>
          <!-- Rules -->
          <svg v-else-if="item.icon === 'rules'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
          </svg>
          <!-- AI -->
          <svg v-else-if="item.icon === 'ai'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
          </svg>
          <!-- Adventure -->
          <svg v-else-if="item.icon === 'adventure'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M9 20l-5.447-2.724A1 1 0 013 16.382V5.618a1 1 0 011.447-.894L9 7m0 13l6-3m-6 3V7m6 10l4.553 2.276A1 1 0 0021 18.382V7.618a1 1 0 00-.553-.894L15 4m0 13V4m0 0L9 7" />
          </svg>
          <!-- Dice -->
          <svg v-else-if="item.icon === 'dice'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
          </svg>
        </span>

        <span v-if="!collapsed" class="truncate">{{ item.label }}</span>
      </RouterLink>
    </nav>

    <!-- Bottom section - collapse toggle -->
    <div class="shrink-0 border-t border-surface-800/60 p-2">
      <button
        class="flex w-full items-center justify-center gap-2 rounded-lg px-3 py-2 text-sm text-gray-500 transition-colors hover:bg-surface-800 hover:text-gray-300"
        :aria-label="collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
        @click="$emit('toggle')"
      >
        <svg
          class="h-4 w-4 transition-transform"
          :class="collapsed ? 'rotate-180' : ''"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M11 19l-7-7 7-7m8 14l-7-7 7-7" />
        </svg>
        <span v-if="!collapsed" class="text-xs">Collapse</span>
      </button>
    </div>
  </aside>
</template>

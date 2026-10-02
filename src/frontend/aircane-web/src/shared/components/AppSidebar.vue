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

// `iconSrc` points at a PNG under /assets/ui for the 8 mapped nav items.
// Dashboard has no PNG asset, so it keeps the inline SVG (iconSrc undefined).
const navItems: { label: string; to: string; icon: string; iconSrc?: string }[] = [
  { label: 'Dashboard', to: '/', icon: 'dashboard' },
  { label: 'Campaigns', to: '/campaigns', icon: 'campaigns', iconSrc: '/assets/ui/icon-campaign.png' },
  { label: 'Library', to: '/library', icon: 'library', iconSrc: '/assets/ui/icon-library.png' },
  { label: 'Characters', to: '/characters', icon: 'characters', iconSrc: '/assets/ui/icon-character.png' },
  { label: 'Rules Lookup', to: '/rules-lookup', icon: 'rules', iconSrc: '/assets/ui/icon-rules.png' },
  { label: 'AI DM', to: '/settings/ai', icon: 'ai', iconSrc: '/assets/ui/icon-ai.png' },
  { label: 'Adventure Forge', to: '/adventures/generate', icon: 'adventure', iconSrc: '/assets/ui/icon-adventure.png' },
  { label: 'Dice Roller', to: '/sessions', icon: 'dice', iconSrc: '/assets/ui/icon-dice.png' },
  { label: 'About & Credits', to: '/about', icon: 'about', iconSrc: '/assets/ui/icon-about.png' },
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
    style="background-image: url('/assets/ui/side-nav-background.png'); background-size: cover; background-position: center;"
  >
    <!-- Logo area -->
    <div class="flex h-14 shrink-0 items-center gap-3 border-b border-surface-800/60 px-4">
      <img
        src="/assets/ui/app-logo-mark.png"
        alt="Aircane Tabletop"
        class="h-10 w-10 shrink-0"
        style="object-fit: contain;"
      />
      <span
        v-if="!collapsed"
        class="text-lg font-bold tracking-tight text-white"
      >
        Aircane Tabletop
      </span>
    </div>

    <!-- Navigation -->
    <nav class="flex-1 space-y-1 overflow-y-auto px-2 py-4" aria-label="Main navigation">
      <RouterLink
        v-for="item in navItems"
        :key="item.to"
        :to="item.to"
        class="nav-item group flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-all duration-150"
        :class="[
          isActive(item.to)
            ? 'active text-aircane-300 shadow-sm'
            : 'text-gray-400 hover:bg-surface-800 hover:text-gray-200',
        ]"
        :aria-current="isActive(item.to) ? 'page' : undefined"
      >
        <!-- Icons -->
        <span class="flex h-5 w-5 shrink-0 items-center justify-center">
          <!-- PNG icon for the mapped nav items -->
          <img
            v-if="item.iconSrc"
            :src="item.iconSrc"
            alt=""
            class="h-6 w-6"
            style="object-fit: contain;"
          />
          <!-- Dashboard keeps its inline SVG (no PNG asset exists) -->
          <svg v-else-if="item.icon === 'dashboard'" class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="1.5" d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
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

<style scoped>
.nav-item.active {
  background: rgba(124, 58, 237, 0.15);
  border-left: 3px solid var(--color-purple);
  box-shadow: inset 0 0 20px rgba(124, 58, 237, 0.1);
}
</style>

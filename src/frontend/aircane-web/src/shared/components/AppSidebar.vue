<script setup lang="ts">
import { useRoute } from 'vue-router'
import AircaneImg from './AircaneImg.vue'
import NavIcon from './NavIcon.vue'

defineProps<{
  collapsed: boolean
}>()

defineEmits<{
  toggle: []
}>()

const route = useRoute()

// `iconName` is the PNG basename under /assets/ui for the 8 mapped nav items
// (rendered via <NavIcon> with an emoji fallback). Dashboard has no PNG asset,
// so it keeps the inline SVG (iconName undefined).
const navItems: { label: string; to: string; icon: string; iconName?: string }[] = [
  { label: 'Dashboard', to: '/', icon: 'dashboard' },
  { label: 'Campaigns', to: '/campaigns', icon: 'campaigns', iconName: 'icon-campaign' },
  { label: 'Library', to: '/library', icon: 'library', iconName: 'icon-library' },
  { label: 'Characters', to: '/characters', icon: 'characters', iconName: 'icon-character' },
  { label: 'Rules Lookup', to: '/rules-lookup', icon: 'rules', iconName: 'icon-rules' },
  { label: 'AI DM', to: '/settings/ai', icon: 'ai', iconName: 'icon-ai' },
  { label: 'Adventure Forge', to: '/adventures/generate', icon: 'adventure', iconName: 'icon-adventure' },
  { label: 'Dice Roller', to: '/sessions', icon: 'dice', iconName: 'icon-dice' },
  { label: 'About & Credits', to: '/about', icon: 'about', iconName: 'icon-about' },
]

function isActive(to: string): boolean {
  if (to === '/') return route.path === '/'
  return route.path.startsWith(to)
}
</script>

<template>
  <aside
    class="flex h-full shrink-0 flex-col border-r border-surface-800/60 bg-surface-900 transition-all duration-300"
    :class="collapsed ? 'w-16' : 'sidebar-expanded'"
    v-bg-asset="{ url: '/assets/ui/side-nav-background.png', fallback: '#0a0a1a' }"
  >
    <!-- Logo area -->
    <div class="flex h-16 shrink-0 items-center gap-3 border-b border-surface-800/60 px-4 pt-2">
      <div class="app-logo-mark shrink-0">
        <AircaneImg
          src="/assets/ui/app-logo-mark.png"
          alt="Aircane Tabletop"
          type="icon"
          fallback-color="transparent"
          fallback-text="✦"
        />
      </div>
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
          <!-- PNG icon (with emoji fallback) for the mapped nav items -->
          <NavIcon
            v-if="item.iconName"
            :icon="item.iconName"
            :label="item.label"
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
/* Expanded sidebar is pinned to exactly 220px on every route (the component is
   shared, so Dashboard and all other pages get the identical width). */
.sidebar-expanded {
  width: 220px;
}
.nav-item.active {
  background: rgba(124, 58, 237, 0.15);
  border-left: 3px solid var(--color-purple);
  box-shadow: inset 0 0 20px rgba(124, 58, 237, 0.1);
}
/* Pin the logo mark to a fixed 40x40 square. The inner AircaneImg wrapper
   and its <img> fill this box, and object-fit:contain keeps the square
   logo whole (no cropping, no overflow onto the nav list below). */
.app-logo-mark {
  width: 40px;
  height: 40px;
  flex: 0 0 auto;
}
.app-logo-mark :deep(.aircane-img) {
  width: 100%;
  height: 100%;
}
.app-logo-mark :deep(img) {
  object-fit: contain;
}
</style>

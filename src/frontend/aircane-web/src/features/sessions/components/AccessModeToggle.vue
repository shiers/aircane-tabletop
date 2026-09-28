<script setup lang="ts">
/**
 * AccessModeToggle - lets the host choose how players connect to a session:
 *   - Local network only (LAN, default)
 *   - Internet play (via Cloudflare Tunnel)
 *
 * Emits the selected SessionAccessMode via v-model. Internet play is only
 * offered inside the desktop wrapper (the tunnel needs the bundled cloudflared
 * sidecar); in a plain browser that option is disabled with an explanation.
 */
import { computed } from 'vue'
import { SessionAccessMode } from '../api'
import { isDesktop } from '@/shared/tauri/bridge'

const props = defineProps<{
  modelValue: SessionAccessMode
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SessionAccessMode): void
}>()

const desktop = isDesktop()

const isLan = computed(() => props.modelValue !== SessionAccessMode.InternetTunnel)
const isInternet = computed(() => props.modelValue === SessionAccessMode.InternetTunnel)

function selectLan(): void {
  emit('update:modelValue', SessionAccessMode.LocalLan)
}

function selectInternet(): void {
  if (!desktop) return
  emit('update:modelValue', SessionAccessMode.InternetTunnel)
}
</script>

<template>
  <fieldset>
    <legend class="mb-2 text-xs font-medium uppercase tracking-wider text-gray-500">
      Access Mode
    </legend>
    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
      <!-- Local network only -->
      <button
        type="button"
        :aria-pressed="isLan"
        class="rounded-lg border p-4 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400"
        :class="isLan
          ? 'border-aircane-400 bg-aircane-950/40'
          : 'border-gray-700 bg-gray-800 hover:border-gray-500'"
        @click="selectLan"
      >
        <span class="block text-sm font-semibold text-white">Local network only</span>
        <span class="mt-1 block text-xs text-gray-400">
          Players join over your LAN. No third party involved.
        </span>
      </button>

      <!-- Internet play -->
      <button
        type="button"
        :aria-pressed="isInternet"
        :disabled="!desktop"
        class="rounded-lg border p-4 text-left transition-colors focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50"
        :class="isInternet
          ? 'border-aircane-400 bg-aircane-950/40'
          : 'border-gray-700 bg-gray-800 hover:border-gray-500'"
        @click="selectInternet"
      >
        <span class="block text-sm font-semibold text-white">
          Internet play
          <span class="font-normal text-gray-400">(via Cloudflare Tunnel)</span>
        </span>
        <span class="mt-1 block text-xs text-gray-400">
          Remote players join over the internet. Routed through Cloudflare.
        </span>
        <span v-if="!desktop" class="mt-2 block text-xs text-yellow-500">
          Available only in the desktop app.
        </span>
      </button>
    </div>
  </fieldset>
</template>

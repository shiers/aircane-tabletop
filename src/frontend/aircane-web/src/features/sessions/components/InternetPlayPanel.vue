<script setup lang="ts">
/**
 * InternetPlayPanel - the host's internet-play control surface.
 *
 * Shows the Cloudflare Tunnel disclosure (once, dismissible), a Start/Stop
 * tunnel button, live status, and — once active — the public tunnel URL with a
 * copy button and QR code.
 *
 * The tunnel is driven by the desktop wrapper's Rust commands
 * (enable_internet_play / disable_internet_play). In a plain browser the tunnel
 * cannot be started; the panel explains that.
 */
import { ref, computed, onMounted } from 'vue'
import {
  isDesktop,
  enableInternetPlay,
  disableInternetPlay,
} from '@/shared/tauri/bridge'
import { getNetworkInfo } from '../api'
import QRCode from './QRCode.vue'

const props = defineProps<{
  /** Whether a session is currently active — the tunnel is only useful with one. */
  sessionActive: boolean
}>()

// ---------------------------------------------------------------------------
// Disclosure ("Don't show again" persisted in localStorage)
// ---------------------------------------------------------------------------

const DISCLOSURE_KEY = 'aircane.internetPlay.disclosureDismissed'
const CLOUDFLARE_PRIVACY_URL = 'https://www.cloudflare.com/privacypolicy/'

const disclosureDismissed = ref<boolean>(
  typeof localStorage !== 'undefined' && localStorage.getItem(DISCLOSURE_KEY) === 'true',
)
const dontShowAgain = ref(false)

function dismissDisclosure(): void {
  if (dontShowAgain.value && typeof localStorage !== 'undefined') {
    localStorage.setItem(DISCLOSURE_KEY, 'true')
  }
  disclosureDismissed.value = true
}

// ---------------------------------------------------------------------------
// Tunnel status
// ---------------------------------------------------------------------------

type TunnelStatus = 'stopped' | 'starting' | 'active'

const desktop = isDesktop()
const status = ref<TunnelStatus>('stopped')
const tunnelUrl = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const urlCopied = ref(false)

const canStart = computed(
  () => desktop && props.sessionActive && status.value === 'stopped',
)

async function startTunnel(): Promise<void> {
  errorMessage.value = null
  status.value = 'starting'
  try {
    const url = await enableInternetPlay()
    if (url) {
      tunnelUrl.value = url
      status.value = 'active'
    } else {
      status.value = 'stopped'
      errorMessage.value = desktop
        ? 'The tunnel did not start. Please try again.'
        : 'Internet play is only available in the desktop app.'
    }
  } catch (err) {
    status.value = 'stopped'
    errorMessage.value = err instanceof Error ? err.message : 'Failed to start the tunnel.'
  }
}

async function stopTunnel(): Promise<void> {
  errorMessage.value = null
  try {
    await disableInternetPlay()
  } finally {
    tunnelUrl.value = null
    status.value = 'stopped'
  }
}

async function copyUrl(): Promise<void> {
  if (!tunnelUrl.value) return
  try {
    await navigator.clipboard.writeText(tunnelUrl.value)
    urlCopied.value = true
    setTimeout(() => {
      urlCopied.value = false
    }, 2000)
  } catch {
    // Clipboard unavailable — silently ignore.
  }
}

const statusLabel = computed(() => {
  switch (status.value) {
    case 'starting':
      return 'Starting…'
    case 'active':
      return 'Active'
    default:
      return 'Stopped'
  }
})

const statusClass = computed(() => {
  switch (status.value) {
    case 'starting':
      return 'text-yellow-400'
    case 'active':
      return 'text-green-400'
    default:
      return 'text-gray-500'
  }
})

// On mount, reflect any tunnel already active on the backend (e.g. after a UI reload).
onMounted(async () => {
  try {
    const info = await getNetworkInfo()
    if (info.tunnelActive && info.tunnelUrl) {
      tunnelUrl.value = info.tunnelUrl
      status.value = 'active'
    }
  } catch {
    // Non-critical.
  }
})
</script>

<template>
  <section
    aria-labelledby="internet-play-heading"
    class="rounded-xl border border-gray-800 bg-gray-900 p-6"
  >
    <div class="mb-4 flex items-start justify-between gap-4">
      <div>
        <h2 id="internet-play-heading" class="text-lg font-semibold text-white">
          Internet Play
        </h2>
        <p class="mt-0.5 text-sm text-gray-400">
          Let remote players join over the internet via Cloudflare Tunnel.
        </p>
      </div>
      <span class="shrink-0 text-sm">
        Status: <span :class="statusClass" class="font-medium">{{ statusLabel }}</span>
      </span>
    </div>

    <!-- Disclosure (B.4) -->
    <div
      v-if="!disclosureDismissed"
      class="mb-4 rounded-lg border border-aircane-800 bg-aircane-950/40 p-4"
    >
      <h3 class="text-sm font-semibold text-white">How internet play works</h3>
      <p class="mt-2 text-sm text-gray-300">
        Aircane uses Cloudflare Tunnel to make your session reachable over the internet
        without router configuration. When internet play is active:
      </p>
      <ul class="mt-2 list-disc space-y-1 pl-5 text-sm text-gray-300">
        <li>Player connections are routed through Cloudflare's network.</li>
        <li>
          Session traffic (chat, dice rolls, narration, character state) passes through
          Cloudflare's servers.
        </li>
        <li>
          Your imported PDFs and source files
          <strong class="text-white">never leave your machine</strong> — only API responses
          are transmitted.
        </li>
        <li>
          The tunnel URL is temporary and changes each session. Share it only with your
          players.
        </li>
      </ul>
      <p class="mt-2 text-sm text-gray-300">
        For private groups who prefer no third-party involvement, use
        <strong class="text-white">Local network only</strong> mode and connect via LAN.
      </p>
      <p class="mt-2 text-xs text-gray-500">
        <a
          :href="CLOUDFLARE_PRIVACY_URL"
          target="_blank"
          rel="noopener noreferrer"
          class="text-aircane-300 underline hover:text-aircane-200"
        >
          Cloudflare privacy policy
        </a>
      </p>

      <div class="mt-3 flex items-center justify-between gap-4">
        <label class="flex items-center gap-2 text-sm text-gray-400">
          <input
            v-model="dontShowAgain"
            type="checkbox"
            class="rounded border-gray-600 bg-gray-800 text-aircane-500 focus:ring-aircane-400"
          />
          Don't show again
        </label>
        <button
          type="button"
          class="rounded-md border border-gray-700 bg-gray-800 px-3 py-1.5 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
          @click="dismissDisclosure"
        >
          Got it
        </button>
      </div>
    </div>

    <!-- Error -->
    <div
      v-if="errorMessage"
      role="alert"
      class="mb-4 rounded-lg border border-red-800 bg-red-950 px-4 py-3 text-sm text-red-300"
    >
      {{ errorMessage }}
    </div>

    <!-- Not in desktop -->
    <p v-if="!desktop" class="text-sm text-yellow-500">
      Internet play requires the Aircane desktop app, which bundles the Cloudflare Tunnel
      client. In a browser, use Local network only mode.
    </p>

    <!-- Controls -->
    <div v-else>
      <div v-if="status !== 'active'" class="flex items-center gap-3">
        <button
          type="button"
          :disabled="!canStart"
          class="rounded-md border border-aircane-700 bg-aircane-900 px-4 py-2 text-sm font-medium text-aircane-100 hover:bg-aircane-800 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:cursor-not-allowed disabled:opacity-50 transition-colors"
          @click="startTunnel"
        >
          <span v-if="status === 'starting'">Starting…</span>
          <span v-else>Start tunnel</span>
        </button>
        <p v-if="!sessionActive" class="text-xs text-gray-500">
          Start a session first, then start the tunnel.
        </p>
      </div>

      <!-- Active: show URL + QR -->
      <div v-else class="flex flex-col gap-6 sm:flex-row sm:items-start">
        <QRCode v-if="tunnelUrl" :value="tunnelUrl" :size="180" />

        <div class="flex-1 space-y-4">
          <div>
            <label class="mb-1 block text-xs font-medium uppercase tracking-wider text-gray-500">
              Internet URL
            </label>
            <div class="flex items-center gap-2">
              <code
                class="flex-1 overflow-x-auto whitespace-nowrap rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-aircane-300"
              >
                {{ tunnelUrl }}
              </code>
              <button
                type="button"
                class="shrink-0 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
                :aria-label="urlCopied ? 'Copied!' : 'Copy internet URL'"
                @click="copyUrl"
              >
                <span v-if="urlCopied" class="text-green-400">✓ Copied</span>
                <span v-else>Copy</span>
              </button>
            </div>
            <p class="mt-1.5 text-xs text-gray-500">
              Share this temporary URL with your remote players. It changes each session.
            </p>
          </div>

          <button
            type="button"
            class="rounded-md border border-red-800 bg-red-950 px-3 py-1.5 text-sm font-medium text-red-300 hover:bg-red-900 focus:outline-none focus:ring-2 focus:ring-red-500 transition-colors"
            @click="stopTunnel"
          >
            Stop tunnel
          </button>
        </div>
      </div>
    </div>
  </section>
</template>

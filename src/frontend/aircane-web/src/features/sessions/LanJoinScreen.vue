<script setup lang="ts">
/**
 * LanJoinScreen — shown to the host after a LAN session is created.
 * Displays the join URL, invite code, and a QR code so local players
 * can join from their own devices.
 */
import { ref, computed, onMounted, watch } from 'vue'
import QRCode from 'qrcode'
import type { SessionDto } from './api'

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

const props = defineProps<{
  session: SessionDto
  /** Absolute join URL to display and encode in the QR code. */
  joinUrl: string
  /** Plain-text invite code (only available immediately after session creation). */
  inviteCode: string
}>()

// ---------------------------------------------------------------------------
// QR code canvas
// ---------------------------------------------------------------------------

const qrCanvas = ref<HTMLCanvasElement | null>(null)
const qrError = ref<string | null>(null)

async function renderQrCode(): Promise<void> {
  if (!qrCanvas.value || !props.joinUrl) return
  qrError.value = null
  try {
    await QRCode.toCanvas(qrCanvas.value, props.joinUrl, {
      width: 200,
      margin: 2,
      color: {
        dark: '#ffffff',
        light: '#111827', // gray-900 to match the card background
      },
    })
  } catch {
    qrError.value = 'Could not generate QR code.'
  }
}

onMounted(renderQrCode)
watch(() => props.joinUrl, renderQrCode)

// ---------------------------------------------------------------------------
// Copy helpers
// ---------------------------------------------------------------------------

const urlCopied = ref(false)
const codeCopied = ref(false)

async function copyToClipboard(text: string, flag: typeof urlCopied): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    flag.value = true
    setTimeout(() => {
      flag.value = false
    }, 2000)
  } catch {
    // Clipboard API not available — silently ignore
  }
}

// ---------------------------------------------------------------------------
// Computed display values
// ---------------------------------------------------------------------------

const statusLabel = computed(() => {
  const map: Record<number, string> = {
    0: 'Pending',
    1: 'Active',
    2: 'Paused',
    3: 'Ended',
  }
  return map[props.session.status] ?? 'Unknown'
})

const statusClass = computed(() => {
  const map: Record<number, string> = {
    0: 'text-yellow-400',
    1: 'text-green-400',
    2: 'text-orange-400',
    3: 'text-gray-500',
  }
  return map[props.session.status] ?? 'text-gray-400'
})
</script>

<template>
  <section
    aria-labelledby="lan-join-heading"
    class="rounded-xl border border-gray-800 bg-gray-900 p-6"
  >
    <!-- Session name and status -->
    <div class="mb-6 flex items-start justify-between gap-4">
      <div>
        <h2 id="lan-join-heading" class="text-lg font-semibold text-white">
          {{ session.name }}
        </h2>
        <p class="mt-0.5 text-sm">
          Status:
          <span :class="statusClass" class="font-medium">{{ statusLabel }}</span>
        </p>
      </div>
      <span
        class="shrink-0 rounded-full border border-gray-700 px-3 py-1 text-xs font-medium text-gray-400"
      >
        LAN Session
      </span>
    </div>

    <div class="flex flex-col gap-6 sm:flex-row sm:items-start">
      <!-- QR code -->
      <div class="flex flex-col items-center gap-2">
        <canvas
          ref="qrCanvas"
          aria-label="QR code for the session join URL"
          class="rounded-lg"
        />
        <p v-if="qrError" role="alert" class="text-xs text-red-400">{{ qrError }}</p>
        <p class="text-xs text-gray-500">Scan to join</p>
      </div>

      <!-- Join details -->
      <div class="flex-1 space-y-4">
        <!-- Join URL -->
        <div>
          <label class="mb-1 block text-xs font-medium uppercase tracking-wider text-gray-500">
            Join URL
          </label>
          <div class="flex items-center gap-2">
            <code
              class="flex-1 overflow-x-auto rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-aircane-300 whitespace-nowrap"
            >
              {{ joinUrl }}
            </code>
            <button
              type="button"
              class="shrink-0 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
              :aria-label="urlCopied ? 'Copied!' : 'Copy join URL'"
              @click="copyToClipboard(joinUrl, urlCopied)"
            >
              <span v-if="urlCopied" class="text-green-400">✓ Copied</span>
              <span v-else>Copy</span>
            </button>
          </div>
        </div>

        <!-- Invite code -->
        <div>
          <label class="mb-1 block text-xs font-medium uppercase tracking-wider text-gray-500">
            Invite Code
          </label>
          <div class="flex items-center gap-2">
            <code
              class="flex-1 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-center text-2xl font-bold tracking-[0.3em] text-white"
            >
              {{ inviteCode }}
            </code>
            <button
              type="button"
              class="shrink-0 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400 transition-colors"
              :aria-label="codeCopied ? 'Copied!' : 'Copy invite code'"
              @click="copyToClipboard(inviteCode, codeCopied)"
            >
              <span v-if="codeCopied" class="text-green-400">✓ Copied</span>
              <span v-else>Copy</span>
            </button>
          </div>
          <p class="mt-1.5 text-xs text-gray-500">
            Share this code with players so they can join the session.
          </p>
        </div>

        <!-- Participant count -->
        <p class="text-sm text-gray-400">
          <span class="font-medium text-white">{{ session.participantCount }}</span>
          {{ session.participantCount === 1 ? 'player' : 'players' }} connected
        </p>
      </div>
    </div>
  </section>
</template>

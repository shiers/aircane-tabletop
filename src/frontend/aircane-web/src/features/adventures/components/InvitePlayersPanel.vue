<script setup lang="ts">
/**
 * InvitePlayersPanel - how the host brings more people into a group adventure that is already running.
 *
 * The lobby shows the join link, QR code and invite code; once the adventure starts the run view
 * replaces the lobby, so this panel keeps them within reach. It is collapsible: open on a wide screen,
 * closed on a phone, with a count on the header so that something waiting for the host is visible
 * even while it is closed.
 *
 * It holds everything needed to get a late player into the run:
 *   join link, QR code and invite code  ->  the player joins  ->  Approve (or Deny)  ->  Add to the end.
 * (The server only lets an approved Player of the run's own play session be added.)
 */
import { ref, computed } from 'vue'
import type { ParticipantDto } from '@/features/sessions/api'
import type { JoinLink } from '@/features/sessions/lobby'
import QRCode from '@/features/sessions/components/QRCode.vue'
import ReplaceSeatControl from '@/features/sessions/components/ReplaceSeatControl.vue'
import { isNarrowScreen } from '@/shared/screen'

const props = defineProps<{
  /** The plain invite code, or null when this browser no longer has it. */
  inviteCode: string | null
  joinLink: JoinLink
  /** People who have joined and are waiting to be approved. */
  pending: ParticipantDto[]
  /** Approved players who are not in the adventure's turn order yet. */
  latePlayers: ParticipantDto[]
  /** A host action (approve or add) is in flight. */
  busy?: boolean
  /** Everyone on the host's roster, to name the participant a same-name request would replace. */
  roster?: ParticipantDto[]
}>()

const emit = defineEmits<{
  (e: 'approve', participantId: string): void
  (e: 'deny', participantId: string): void
  (e: 'replace', newParticipantId: string, oldParticipantId: string): void
  (e: 'add', participantId: string): void
}>()

// Open on a wide screen, closed on a phone.
const open = ref(!isNarrowScreen())

/** Things waiting for the host: requests to approve, and approved players to add. */
const attentionCount = computed(() => props.pending.length + props.latePlayers.length)

const attentionLabel = computed(() => {
  const parts: string[] = []
  if (props.pending.length) parts.push(`${props.pending.length} waiting for approval`)
  if (props.latePlayers.length) parts.push(`${props.latePlayers.length} to add to the adventure`)
  return parts.join(', ')
})

// ── Copy buttons ─────────────────────────────────────────────────────────────

const copied = ref<'url' | 'code' | null>(null)
let copiedTimer: ReturnType<typeof setTimeout> | null = null

async function copy(text: string, which: 'url' | 'code'): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = which
    if (copiedTimer) clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => {
      copied.value = null
    }, 2000)
  } catch {
    // The Clipboard API is not available (or was refused); the text is still on screen to select.
  }
}
</script>

<template>
  <section class="rounded-xl border border-surface-700/50 bg-surface-900" data-testid="invite-panel">
    <h3 class="m-0">
      <button
        type="button"
        class="flex w-full items-center justify-between gap-3 rounded-xl px-4 py-3 text-left focus:outline-none focus:ring-2 focus:ring-aircane-400"
        :aria-expanded="open"
        aria-controls="invite-panel-body"
        data-testid="invite-toggle"
        @click="open = !open"
      >
        <span class="flex min-w-0 items-center gap-2">
          <span class="text-sm font-semibold text-white">Invite players</span>
          <span
            v-if="attentionCount > 0"
            class="inline-flex min-w-[1.5rem] shrink-0 items-center justify-center rounded-full bg-yellow-500 px-2 py-0.5 text-xs font-bold text-yellow-950"
            data-testid="invite-badge"
            :title="attentionLabel"
          >
            {{ attentionCount }}
            <span class="sr-only"> need your attention: {{ attentionLabel }}</span>
          </span>
        </span>
        <span class="shrink-0 text-gray-400" aria-hidden="true">{{ open ? '▲' : '▼' }}</span>
      </button>
    </h3>

    <div v-show="open" id="invite-panel-body" class="space-y-5 border-t border-surface-700/50 px-4 py-4" data-testid="invite-body">
      <!-- Join details -->
      <div class="flex flex-col gap-4 sm:flex-row sm:items-start">
        <div class="flex flex-col items-center gap-1 sm:shrink-0">
          <QRCode :value="joinLink.url" :size="160" />
          <p class="text-xs text-gray-500">Scan to join</p>
        </div>

        <div class="min-w-0 flex-1 space-y-4">
          <div>
            <p class="mb-1 text-xs font-medium uppercase tracking-wider text-gray-500">Join link</p>
            <div class="flex items-center gap-2">
              <code
                class="min-w-0 flex-1 overflow-x-auto whitespace-nowrap rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm text-aircane-300"
                data-testid="invite-url"
              >{{ joinLink.url }}</code>
              <button
                type="button"
                class="shrink-0 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
                :aria-label="copied === 'url' ? 'Copied' : 'Copy join link'"
                data-testid="copy-url"
                @click="copy(joinLink.url, 'url')"
              >
                <span v-if="copied === 'url'" class="text-green-400">✓ Copied</span>
                <span v-else>Copy</span>
              </button>
            </div>
            <p v-if="!joinLink.reachableFromLan" class="mt-1 text-xs text-amber-300" data-testid="invite-no-lan">
              Couldn't find this computer's address on your network, so this link only works on this computer.
            </p>
          </div>

          <div v-if="inviteCode">
            <p class="mb-1 text-xs font-medium uppercase tracking-wider text-gray-500">Invite code</p>
            <div class="flex items-center gap-2">
              <code
                class="min-w-0 flex-1 break-all rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-center text-xl font-bold tracking-[0.2em] text-white"
                data-testid="invite-code"
              >{{ inviteCode }}</code>
              <button
                type="button"
                class="shrink-0 rounded-md border border-gray-700 bg-gray-800 px-3 py-2 text-sm font-medium text-gray-300 hover:border-gray-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-aircane-400"
                :aria-label="copied === 'code' ? 'Copied' : 'Copy invite code'"
                data-testid="copy-code"
                @click="copy(inviteCode, 'code')"
              >
                <span v-if="copied === 'code'" class="text-green-400">✓ Copied</span>
                <span v-else>Copy</span>
              </button>
            </div>
          </div>

          <!-- The server returns the code once, at creation, and keeps only a hash. -->
          <div v-else class="space-y-2 rounded-lg border border-amber-700 bg-amber-950/30 px-3 py-3 text-sm text-amber-200" data-testid="invite-code-missing">
            <p>
              The invite code can't be shown again. The server keeps only a hash of it, and the copy this browser
              saved when the session was created is gone (site data was cleared, this is a different browser, or the
              session is older than this feature). Players who already have the code can still join.
            </p>
            <button
              type="button"
              disabled
              class="cursor-not-allowed rounded-md border border-gray-600 bg-gray-800 px-3 py-1.5 text-sm font-medium text-gray-400 opacity-60"
              aria-describedby="new-session-reason"
              data-testid="new-session-button"
            >
              Create a new play session
            </button>
            <p id="new-session-reason" class="text-xs text-amber-100/80" data-testid="new-session-reason">
              Unavailable while this adventure is running. A new play session can be created, but the adventure stays
              tied to the one it started in: players who join a new session can't be added to it. To get a new
              invite code you would have to abandon this adventure and start a new group session.
            </p>
          </div>
        </div>
      </div>

      <!-- Waiting for approval -->
      <div v-if="pending.length" data-testid="invite-pending">
        <h4 class="mb-2 text-sm font-medium uppercase tracking-wider text-yellow-400">Waiting for approval</h4>
        <ul class="space-y-2">
          <li
            v-for="person in pending"
            :key="person.id"
            class="flex flex-wrap items-center justify-between gap-3 rounded-md border border-yellow-800 bg-yellow-950/30 px-3 py-2"
          >
            <div class="min-w-0 flex-1">
              <span class="block truncate text-sm text-white">{{ person.displayName }}</span>
              <ReplaceSeatControl
                :request="person"
                :roster="roster ?? pending"
                :busy="busy"
                @replace="(newId: string, oldId: string) => emit('replace', newId, oldId)"
              />
            </div>
            <span class="flex shrink-0 gap-2">
              <button
                type="button"
                class="rounded-md border border-red-800 px-3 py-1.5 text-sm font-medium text-red-300 hover:bg-red-950 focus:outline-none focus:ring-2 focus:ring-red-500 disabled:opacity-50"
                :disabled="busy"
                :aria-label="`Deny ${person.displayName}`"
                data-testid="invite-deny"
                @click="emit('deny', person.id)"
              >
                Deny
              </button>
              <button
                type="button"
                class="rounded-md bg-green-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-600 focus:outline-none focus:ring-2 focus:ring-green-500 disabled:opacity-50"
                :disabled="busy"
                :aria-label="`Approve ${person.displayName}`"
                data-testid="invite-approve"
                @click="emit('approve', person.id)"
              >
                Approve
              </button>
            </span>
          </li>
        </ul>
      </div>

      <!-- Approved, but not in the adventure yet -->
      <div v-if="latePlayers.length" data-testid="late-players">
        <h4 class="mb-2 text-sm font-medium uppercase tracking-wider text-yellow-400">Approved, not in the adventure yet</h4>
        <ul class="space-y-2">
          <li
            v-for="person in latePlayers"
            :key="person.id"
            class="flex items-center justify-between gap-3 rounded-md border border-yellow-800 bg-yellow-950/30 px-3 py-2"
          >
            <span class="min-w-0 truncate text-sm text-white">{{ person.displayName }}</span>
            <button
              type="button"
              class="shrink-0 rounded-md bg-green-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-600 focus:outline-none focus:ring-2 focus:ring-green-500 disabled:opacity-50"
              :disabled="busy"
              :aria-label="`Add ${person.displayName} to the end of the turn order`"
              data-testid="add-late-player"
              @click="emit('add', person.id)"
            >
              Add to the end
            </button>
          </li>
        </ul>
        <p class="mt-2 text-xs text-gray-500">
          They are added at the end of the turn order. Whose turn it is, and the round, don't change.
        </p>
      </div>
    </div>
  </section>
</template>

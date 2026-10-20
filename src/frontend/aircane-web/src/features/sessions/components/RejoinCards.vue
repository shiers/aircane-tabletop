<script setup lang="ts">
/**
 * RejoinCards - on the sessions page, one card for every session this browser joined as a player and can go back
 * to (it holds a rejoin secret for it; see rejoinSecrets.ts).
 *
 * On load each secret is checked with the status-only call, which logs nobody in: a card shows the session's name,
 * the player's name and where they stand. Secrets for sessions that ended, that the server does not know, where
 * the host declined the request, or that the host removed the player from, are removed without a word. One click on "Rejoin" takes the seat back (a fresh
 * token, no new request) and goes to the session (approved) or the waiting screen (pending); a request declined in
 * the meantime is said on the card. A host's own browser has no participant record, so its sessions never appear.
 */
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { getRejoinStatus, type RejoinStatusValue } from '../api'
import { DENIED_MESSAGE, REMOVED_MESSAGE, mapRejoinError, rejoinFailure } from '../playerErrors'
import { forgetRejoinSecret, listRejoinSecrets, rememberRejoinName } from '../rejoinSecrets'
import { rejoinWithStoredSecret } from '../rejoin'

const props = defineProps<{
  /** Test seam: replaces the status call. */
  checkStatus?: typeof getRejoinStatus
  /** Test seam: replaces the rejoin call. */
  rejoin?: typeof rejoinWithStoredSecret
}>()

interface Card {
  sessionId: string
  displayName: string
  /** Null until the host has answered. */
  sessionName: string | null
  status: RejoinStatusValue | null
  /** Shown on the card: the host cannot be reached, the session ended in the meantime, ... */
  problem: string | null
  busy: boolean
  /** The seat is gone (declined or ended): no button any more. */
  closed: boolean
}

const router = useRouter()
const cards = ref<Card[]>([])
const checking = ref(false)

const statusLabels: Record<RejoinStatusValue, string> = {
  approved: 'In the session',
  pending: 'Waiting for the host to approve you',
  denied: 'Declined by the host',
}

async function checkAll(): Promise<void> {
  const entries = listRejoinSecrets()
  if (entries.length === 0) return
  checking.value = true
  cards.value = entries.map((entry) => ({
    sessionId: entry.sessionId,
    displayName: entry.displayName,
    sessionName: null,
    status: null,
    problem: null,
    busy: false,
    closed: false,
  }))

  const check = props.checkStatus ?? getRejoinStatus
  const results = await Promise.allSettled(entries.map((entry) => check(entry.sessionId, entry.secret)))

  const kept: Card[] = []
  results.forEach((outcome, index) => {
    const card = cards.value[index]
    if (outcome.status === 'fulfilled') {
      if (outcome.value.status === 'denied') {
        forgetRejoinSecret(card.sessionId)   // nothing to go back to
        return
      }
      rememberRejoinName(card.sessionId, outcome.value.displayName)
      kept.push({ ...card, sessionName: outcome.value.sessionName, displayName: outcome.value.displayName, status: outcome.value.status })
      return
    }
    const failure = rejoinFailure(outcome.reason)
    if (failure === 'gone' || failure === 'denied' || failure === 'removed') {
      forgetRejoinSecret(card.sessionId)   // ended, unknown, declined or removed: no card
      return
    }
    kept.push({ ...card, problem: mapRejoinError(outcome.reason) })
  })
  cards.value = kept
  checking.value = false
}

async function rejoin(card: Card): Promise<void> {
  if (card.busy || card.closed) return
  card.busy = true
  card.problem = null
  try {
    const outcome = await (props.rejoin ?? rejoinWithStoredSecret)(card.sessionId)
    if (outcome.kind === 'rejoined') {
      await router.push(outcome.result.isApproved
        ? { name: 'play-session', params: { sessionId: card.sessionId } }
        : { name: 'join-session', params: { sessionId: card.sessionId }, query: { returning: '1' } })
      return
    }
    if (outcome.kind === 'none') {
      card.closed = true
      card.problem = 'This browser no longer has a place in that session. Join with the invite code.'
      return
    }
    if (outcome.failure === 'denied') {
      card.closed = true
      card.status = 'denied'
      card.problem = DENIED_MESSAGE
    } else if (outcome.failure === 'removed') {
      card.closed = true
      card.problem = REMOVED_MESSAGE
    } else if (outcome.failure === 'gone') {
      card.closed = true
      card.problem = outcome.message
    } else {
      card.problem = outcome.message
    }
  } finally {
    card.busy = false
  }
}

onMounted(checkAll)
</script>

<template>
  <section
    v-if="cards.length > 0 || checking"
    aria-labelledby="rejoin-heading"
    class="space-y-3"
    data-testid="rejoin-cards"
  >
    <h2 id="rejoin-heading" class="text-lg font-semibold text-white">Your sessions</h2>
    <p v-if="checking" class="text-sm text-gray-400" aria-live="polite">Checking with the host…</p>
    <ul v-else class="space-y-3">
      <li
        v-for="card in cards"
        :key="card.sessionId"
        class="min-w-0 rounded-xl border border-aircane-500/60 bg-aircane-900/30 p-4"
        data-testid="rejoin-card"
      >
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="min-w-0 flex-1">
            <p class="truncate font-semibold text-white" data-testid="rejoin-card-session">
              {{ card.sessionName ?? 'A session you joined' }}
            </p>
            <p class="truncate text-sm text-gray-300">
              As <span class="font-medium" data-testid="rejoin-card-name">{{ card.displayName || 'Player' }}</span>
            </p>
            <p v-if="card.status" class="text-xs text-gray-400" data-testid="rejoin-card-status">
              {{ statusLabels[card.status] }}
            </p>
          </div>
          <button
            v-if="!card.closed"
            type="button"
            class="min-h-[44px] shrink-0 rounded-lg bg-aircane-600 px-5 py-2 text-sm font-semibold text-white hover:bg-aircane-500 focus:outline-none focus:ring-2 focus:ring-aircane-400 disabled:opacity-50"
            :disabled="card.busy"
            :aria-label="`Rejoin ${card.sessionName ?? 'the session'} as ${card.displayName || 'Player'}`"
            data-testid="rejoin-button"
            @click="rejoin(card)"
          >
            {{ card.busy ? 'Rejoining…' : 'Rejoin' }}
          </button>
        </div>
        <p v-if="card.problem" role="alert" class="mt-2 text-sm text-orange-300" data-testid="rejoin-card-problem">
          {{ card.problem }}
        </p>
      </li>
    </ul>
  </section>
</template>

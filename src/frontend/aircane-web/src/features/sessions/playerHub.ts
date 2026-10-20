import * as signalR from '@microsoft/signalr'
import { ParticipantRole } from './api'

/**
 * The player's side of the session hub.
 *
 * Two things about the hub are easy to get wrong, and both cost the player their live updates:
 *   - **Joining the group.** `JoinSession` takes one message object `{ sessionId, participantId,
 *     displayName, role }`, not a bare session id. Without a correct join the connection is never
 *     added to the session's group, so no event (approval, chat, rolls, adventure changes) arrives.
 *   - **Joining again after a reconnect.** Group membership belongs to the connection. SignalR's
 *     automatic reconnect opens a new connection that is in no group until it joins again.
 */

export interface PlayerIdentity {
  participantId: string
  displayName: string
}

/** A server → client event handler. The hub sends one notification object per event. */
export type HubHandler = (...args: never[]) => void

export interface PlayerHubOptions {
  sessionId: string
  me: PlayerIdentity
  /** Events to listen for, by the name the server sends them under. */
  handlers: Record<string, HubHandler>
  /** Called when the connection comes up (true) or drops (false). */
  onConnectionChange?: (connected: boolean) => void
  /** Called after an automatic reconnect has rejoined the group, so the view can catch up on what it missed. */
  onReconnected?: () => void
}

export interface PlayerHub {
  connection: signalR.HubConnection
  /** Sends a chat message to the table. */
  sendChat: (text: string) => Promise<void>
  stop: () => Promise<void>
}

/** The message the hub's JoinSession expects. */
export function joinMessage(sessionId: string, me: PlayerIdentity) {
  return {
    sessionId,
    participantId: me.participantId,
    displayName: me.displayName,
    role: ParticipantRole.Player,
  }
}

function buildHubUrl(): string {
  let token: string | null = null
  try {
    token = sessionStorage.getItem('participant_token')
  } catch {
    token = null
  }
  const base = (import.meta.env.VITE_API_BASE_URL ?? '') + '/hubs/session'
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base
}

/**
 * Connects, registers the handlers, and joins the session's group. Rejects if the connection or the
 * join fails (the caller decides whether to retry).
 */
export async function connectPlayerHub(options: PlayerHubOptions): Promise<PlayerHub> {
  const { sessionId, me, handlers, onConnectionChange, onReconnected } = options

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(buildHubUrl())
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  for (const [name, handler] of Object.entries(handlers)) {
    connection.on(name, handler as (...args: unknown[]) => void)
  }

  const join = () => connection.invoke('JoinSession', joinMessage(sessionId, me))

  connection.onreconnecting?.(() => onConnectionChange?.(false))
  connection.onclose?.(() => onConnectionChange?.(false))
  connection.onreconnected?.(async () => {
    try {
      await join()
      onConnectionChange?.(true)
      onReconnected?.()
    } catch {
      // Still not usable; the next reconnect attempt (or the view's poll) takes over.
    }
  })

  await connection.start()
  await join()
  onConnectionChange?.(true)

  return {
    connection,
    sendChat: async (text: string) => {
      await connection.invoke('SendChatMessage', {
        sessionId,
        participantId: me.participantId,
        displayName: me.displayName,
        text,
      })
    },
    stop: async () => {
      try {
        await connection.stop()
      } catch {
        // Already stopped.
      }
    },
  }
}

// ---------------------------------------------------------------------------
// Payloads. The server sends camelCase JSON; `sessionId` of an adventure event is the RUN's id.
// ---------------------------------------------------------------------------

export interface ParticipantApprovedEvent {
  sessionId: string
  participantId: string
  displayName: string
  /** True when the session does not need the host's approval. */
  autoApproved: boolean
}

export interface ParticipantDeniedEvent {
  sessionId: string
  participantId: string
  displayName: string
}

/** Sent to the host and to the person removed, just before that person's connection is closed. */
export interface ParticipantRemovedEvent {
  sessionId: string
  participantId: string
  displayName: string
}

/** Host only: a participant's first connection opened, or their last one closed. */
export interface ParticipantPresenceChangedEvent {
  sessionId: string
  participantId: string
  isConnected: boolean
}

/** The signal sent when a turn, the scene or a player's action changed the adventure; carries no content. */
export interface AdventureStateChangedEvent {
  /** The adventure run's id. */
  sessionId: string
  /** "TurnAdvanced" | "SceneAdvanced" | "PlayerAction". */
  reason: string
  changedAt: string
}

export interface ChatMessageEvent {
  sessionId: string
  participantId: string
  displayName: string
  text: string
  sentAt: string
  /** The stored message's id (the one chat history returns); absent from a server without stored chat. */
  messageId?: string | null
  /** The sender's role name ("Player", "Host"...). */
  role?: string | null
}

export interface RollRequestedEvent {
  sessionId: string
  targetParticipantId: string
  characterId: string | null
  formula: string
  label: string
  dc: number | null
  reason: string | null
}

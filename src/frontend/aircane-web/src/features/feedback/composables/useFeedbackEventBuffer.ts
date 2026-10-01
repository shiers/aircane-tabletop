/**
 * A module-level circular buffer of the most recent SignalR session events, used by the in-app
 * feedback feature so a bug report can include what happened just before the failure.
 *
 * It lives at module scope (a singleton) so it persists regardless of component mount/unmount —
 * the host and player session views push into the same buffer from inside their existing SignalR
 * handlers.
 *
 * SECURITY: only a short, non-sensitive summary string is stored — never full narration text,
 * character JSON, or PDF content. Callers must pass only safe scalar fields as `keyField`.
 */

const CAP = 20

interface BufferedEvent {
  timestamp: string
  eventType: string
  summary: string
}

const buffer: BufferedEvent[] = []

/** Formats a UTC time as HH:mm:ssZ. */
function formatTimestamp(date = new Date()): string {
  return `${date.toISOString().slice(11, 19)}Z`
}

export interface PushEventInput {
  /** Who/what produced the event, e.g. "AI", "Player", "Host". */
  actor: string
  /** The SignalR event name, e.g. "RollRecorded". */
  eventType: string
  /** An optional short, non-sensitive scalar summary (e.g. a roll formula/total). */
  keyField?: string | null
}

/**
 * Records a session event. The summary string is
 * `{HH:mm:ssZ} [{actor}] {eventType} — {keyField}` (the `— ...` is omitted when no keyField).
 */
export function pushEvent(input: PushEventInput): void {
  const timestamp = formatTimestamp()
  const keyField = input.keyField?.toString().trim()
  const summary = keyField
    ? `${timestamp} [${input.actor}] ${input.eventType} — ${keyField}`
    : `${timestamp} [${input.actor}] ${input.eventType}`

  buffer.push({ timestamp, eventType: input.eventType, summary })
  while (buffer.length > CAP) {
    buffer.shift()
  }
}

/** Returns the summary strings for the last 10 events (oldest first). */
export function snapshot(): string[] {
  return buffer.slice(-10).map((e) => e.summary)
}

/** Clears the buffer. Intended for tests. */
export function clearEventBuffer(): void {
  buffer.length = 0
}

/** Composable accessor so views can `useFeedbackEventBuffer()` consistently with the codebase. */
export function useFeedbackEventBuffer() {
  return { pushEvent, snapshot, clearEventBuffer }
}

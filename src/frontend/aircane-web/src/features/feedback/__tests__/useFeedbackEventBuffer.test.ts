import { describe, it, expect, beforeEach } from 'vitest'
import {
  pushEvent,
  snapshot,
  clearEventBuffer,
} from '../composables/useFeedbackEventBuffer'

describe('useFeedbackEventBuffer', () => {
  beforeEach(() => {
    clearEventBuffer()
  })

  it('maintains a circular buffer capped at 20 events', () => {
    for (let i = 0; i < 25; i++) {
      pushEvent({ actor: 'Player', eventType: 'RollRecorded', keyField: `#${i}` })
    }
    // snapshot returns the last 10; the newest should be #24.
    const last = snapshot()
    expect(last).toHaveLength(10)
    expect(last[last.length - 1]).toContain('#24')
    // The oldest surviving event is #15 (25 pushed, 20 kept, last 10 shown => #15..#24).
    expect(last[0]).toContain('#15')
  })

  it('snapshot returns the last 10 events', () => {
    for (let i = 0; i < 12; i++) {
      pushEvent({ actor: 'AI', eventType: 'AINarrationCompleted', keyField: `n${i}` })
    }
    const last = snapshot()
    expect(last).toHaveLength(10)
    expect(last[0]).toContain('n2')
    expect(last[9]).toContain('n11')
  })

  it('formats the summary string with actor, event type, and key field', () => {
    pushEvent({ actor: 'Player', eventType: 'RollRecorded', keyField: '1d20+5 = 17' })
    const [entry] = snapshot()
    expect(entry).toMatch(/^\d{2}:\d{2}:\d{2}Z \[Player\] RollRecorded — 1d20\+5 = 17$/)
  })

  it('omits the dash separator when there is no key field', () => {
    pushEvent({ actor: 'Host', eventType: 'ParticipantJoined' })
    const [entry] = snapshot()
    expect(entry).toContain('[Host] ParticipantJoined')
    expect(entry).not.toContain('—')
  })
})

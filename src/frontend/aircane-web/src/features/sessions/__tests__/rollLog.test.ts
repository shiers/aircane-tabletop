import { describe, it, expect, vi } from 'vitest'
import { RollVisibility, type RollDto } from '../../dice/api'
import { canSeeRoll, describeRoll, mergeRolls, useRollLog, visibleRolls } from '../rollLog'

function roll(overrides: Partial<RollDto> = {}): RollDto {
  return {
    id: 'roll-1',
    sessionId: 'session-1',
    characterId: null,
    rollerParticipantId: 'bifur',
    rollerName: 'Bifur',
    formula: '1d20+2',
    dieResults: [15],
    modifier: 2,
    total: 17,
    isManual: false,
    visibility: RollVisibility.Public,
    context: 'Perception',
    createdAt: '2026-10-20T10:00:00Z',
    ...overrides,
  }
}

const player = { isHost: false, participantId: 'thorin' }
const host = { isHost: true, participantId: null }

describe('who sees a roll', () => {
  it('shows a public roll to everyone', () => {
    expect(canSeeRoll(roll(), player)).toBe(true)
    expect(canSeeRoll(roll(), host)).toBe(true)
  })

  it('shows a private roll to its roller and the host only', () => {
    const secret = roll({ visibility: RollVisibility.Private, rollerParticipantId: 'bifur' })

    expect(canSeeRoll(secret, { isHost: false, participantId: 'bifur' })).toBe(true)
    expect(canSeeRoll(secret, host)).toBe(true)
    expect(canSeeRoll(secret, player)).toBe(false)
    expect(canSeeRoll(secret, { isHost: false, participantId: null })).toBe(false)
  })

  it('filters a list, so a private roll that reached the screen by mistake is not shown', () => {
    const rolls = [
      roll({ id: 'a' }),
      roll({ id: 'b', visibility: RollVisibility.Private }),
      roll({ id: 'c', visibility: RollVisibility.Private, rollerParticipantId: 'thorin' }),
    ]

    expect(visibleRolls(rolls, player).map((r) => r.id)).toEqual(['a', 'c'])
  })
})

describe('describeRoll', () => {
  it('names the roller, the formula, the dice, the modifier and the total', () => {
    expect(describeRoll(roll())).toBe('Bifur: 1d20+2 [15] +2 = 17 (Perception)')
  })

  it('shows several dice and a negative modifier', () => {
    expect(describeRoll(roll({ formula: '2d6-1', dieResults: [3, 4], modifier: -1, total: 6, context: null }))).toBe(
      'Bifur: 2d6-1 [3, 4] -1 = 6',
    )
  })

  it('calls the viewer\'s own rolls "You", and a host roll (no name) "Host"', () => {
    expect(describeRoll(roll({ rollerParticipantId: 'thorin', rollerName: 'Thorin' }), 'thorin')).toMatch(/^You: /)
    expect(describeRoll(roll({ rollerName: null, rollerParticipantId: 'host' }))).toMatch(/^Host: /)
  })

  it('describes a physical-dice roll with no formula', () => {
    expect(describeRoll(roll({ formula: null, dieResults: [12], modifier: 0, total: 12, context: null }))).toBe(
      'Bifur: Physical dice [12] = 12',
    )
  })
})

describe('mergeRolls', () => {
  it('shows a roll once when history and a live event both carry it, newest first', () => {
    const older = roll({ id: 'older', createdAt: '2026-10-20T10:00:00Z' })
    const newer = roll({ id: 'newer', createdAt: '2026-10-20T10:05:00Z' })

    const merged = mergeRolls([older], [newer, older])

    expect(merged.map((r) => r.id)).toEqual(['newer', 'older'])
  })
})

describe('useRollLog', () => {
  it('loads the history and merges it with live rolls, without duplicates', async () => {
    const stored = [roll({ id: 'a', createdAt: '2026-10-20T10:00:00Z' })]
    const log = useRollLog('session-1', () => player, vi.fn().mockResolvedValue(stored))

    log.addLive(roll({ id: 'b', createdAt: '2026-10-20T10:05:00Z' }))   // arrived before the history did
    await log.refresh()
    log.addLive(roll({ id: 'a', createdAt: '2026-10-20T10:00:00Z' }))   // the same roll, live too

    expect(log.rolls.value.map((r) => r.id)).toEqual(['b', 'a'])
  })

  it('loads the history again on demand (the tab was opened, the connection came back) and adds what was missed', async () => {
    const fetchLog = vi.fn().mockResolvedValueOnce([roll({ id: 'a' })]).mockResolvedValueOnce([
      roll({ id: 'c', createdAt: '2026-10-20T11:00:00Z' }),
      roll({ id: 'a' }),
    ])
    const log = useRollLog('session-1', () => player, fetchLog)

    await log.refresh()
    await log.refresh()

    expect(fetchLog).toHaveBeenCalledWith('session-1')
    expect(log.rolls.value.map((r) => r.id)).toEqual(['c', 'a'])
  })

  it('does not show someone else\'s private roll, from history or live', async () => {
    const secret = roll({ id: 'secret', visibility: RollVisibility.Private })
    const log = useRollLog('session-1', () => player, vi.fn().mockResolvedValue([secret, roll({ id: 'open' })]))

    await log.refresh()
    log.addLive(roll({ id: 'secret-live', visibility: RollVisibility.Private }))

    expect(log.rolls.value.map((r) => r.id)).toEqual(['open'])
  })

  it('shows the host everything, and a player their own private roll', async () => {
    const secret = roll({ id: 'secret', visibility: RollVisibility.Private, rollerParticipantId: 'thorin' })

    const asHost = useRollLog('session-1', () => host, vi.fn().mockResolvedValue([secret]))
    await asHost.refresh()
    const asThorin = useRollLog('session-1', () => player, vi.fn().mockResolvedValue([secret]))
    await asThorin.refresh()

    expect(asHost.rolls.value).toHaveLength(1)
    expect(asThorin.rolls.value).toHaveLength(1)
  })

  it('keeps what it has and says so when the history cannot be loaded', async () => {
    const log = useRollLog('session-1', () => player, vi.fn().mockRejectedValue(new Error('offline')))
    log.addLive(roll({ id: 'live' }))

    await log.refresh()

    expect(log.rolls.value.map((r) => r.id)).toEqual(['live'])
    expect(log.error.value).toContain('roll log')
  })
})

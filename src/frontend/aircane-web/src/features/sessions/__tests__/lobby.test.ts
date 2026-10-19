import { describe, it, expect } from 'vitest'
import { ParticipantRole, type NetworkInfo, type ParticipantDto } from '../api'
import {
  buildJoinLink,
  canStartAdventure,
  eligiblePlayers,
  moveInTurnOrder,
  pendingParticipants,
  reconcileTurnOrder,
  startDisabledReason,
  type StartState,
} from '../lobby'

function participant(id: string, overrides: Partial<ParticipantDto> = {}): ParticipantDto {
  return {
    id,
    sessionId: 'session-1',
    displayName: id,
    role: ParticipantRole.Player,
    characterId: null,
    isApproved: true,
    joinedAt: '2024-01-01T10:00:00Z',
    lastSeenAt: '2024-01-01T10:00:00Z',
    ...overrides,
  }
}

describe('ParticipantRole', () => {
  // The API sends the enum as a number; these must match Aircane.Domain.Enums.ParticipantRole.
  it('matches the server numbering', () => {
    expect(ParticipantRole.Host).toBe(0)
    expect(ParticipantRole.HumanDm).toBe(1)
    expect(ParticipantRole.Player).toBe(2)
    expect(ParticipantRole.Spectator).toBe(3)
  })
})

describe('eligiblePlayers', () => {
  it('keeps approved players only', () => {
    const list = [participant('a'), participant('b', { isApproved: false })]
    expect(eligiblePlayers(list).map((p) => p.id)).toEqual(['a'])
  })

  it('leaves out approved participants who are not players', () => {
    const list = [
      participant('player'),
      participant('dm', { role: ParticipantRole.HumanDm }),
      participant('spectator', { role: ParticipantRole.Spectator }),
      participant('host', { role: ParticipantRole.Host }),
    ]
    expect(eligiblePlayers(list).map((p) => p.id)).toEqual(['player'])
  })

  it('is empty for an empty lobby', () => {
    expect(eligiblePlayers([])).toEqual([])
  })
})

describe('pendingParticipants', () => {
  it('lists unapproved people, oldest first', () => {
    const list = [
      participant('late', { isApproved: false, joinedAt: '2024-01-01T10:05:00Z' }),
      participant('early', { isApproved: false, joinedAt: '2024-01-01T10:01:00Z' }),
      participant('done'),
    ]
    expect(pendingParticipants(list).map((p) => p.id)).toEqual(['early', 'late'])
  })
})

describe('reconcileTurnOrder', () => {
  it('starts empty and fills in the order players are approved', () => {
    let order: string[] = []
    order = reconcileTurnOrder(order, [participant('alice')])
    order = reconcileTurnOrder(order, [participant('alice'), participant('bob')])
    order = reconcileTurnOrder(order, [participant('alice'), participant('bob'), participant('cara')])
    expect(order).toEqual(['alice', 'bob', 'cara'])
  })

  it('does not reorder people already placed when the list order differs', () => {
    const order = reconcileTurnOrder(['bob', 'alice'], [participant('alice'), participant('bob')])
    expect(order).toEqual(['bob', 'alice'])
  })

  it('puts players who were already approved on load in join order', () => {
    const order = reconcileTurnOrder([], [
      participant('later', { joinedAt: '2024-01-01T10:09:00Z' }),
      participant('first', { joinedAt: '2024-01-01T10:01:00Z' }),
    ])
    expect(order).toEqual(['first', 'later'])
  })

  it('appends a newcomer after a manual reordering instead of resetting it', () => {
    const order = reconcileTurnOrder(['bob', 'alice'], [participant('alice'), participant('bob'), participant('cara')])
    expect(order).toEqual(['bob', 'alice', 'cara'])
  })

  it('drops someone who is no longer eligible', () => {
    const order = reconcileTurnOrder(['alice', 'bob'], [participant('alice')])
    expect(order).toEqual(['alice'])
  })
})

describe('moveInTurnOrder', () => {
  const order = ['a', 'b', 'c']

  it('moves a player up', () => {
    expect(moveInTurnOrder(order, 'b', -1)).toEqual(['b', 'a', 'c'])
  })

  it('moves a player down', () => {
    expect(moveInTurnOrder(order, 'b', 1)).toEqual(['a', 'c', 'b'])
  })

  it('cannot move the first player up or the last player down', () => {
    expect(moveInTurnOrder(order, 'a', -1)).toEqual(['a', 'b', 'c'])
    expect(moveInTurnOrder(order, 'c', 1)).toEqual(['a', 'b', 'c'])
  })

  it('ignores someone who is not in the order', () => {
    expect(moveInTurnOrder(order, 'zed', 1)).toEqual(['a', 'b', 'c'])
  })

  it('returns a new array and leaves the input alone', () => {
    const result = moveInTurnOrder(order, 'a', 1)
    expect(result).not.toBe(order)
    expect(order).toEqual(['a', 'b', 'c'])
  })
})

describe('Start adventure enabled / disabled', () => {
  const ready: StartState = { turnOrderCount: 2, starting: false, sessionOpen: true, runExists: false }

  it('is enabled with at least one approved player', () => {
    expect(canStartAdventure(ready)).toBe(true)
    expect(canStartAdventure({ ...ready, turnOrderCount: 1 })).toBe(true)
    expect(startDisabledReason(ready)).toBeNull()
  })

  it('is disabled until a player is approved, and says so', () => {
    const state = { ...ready, turnOrderCount: 0 }
    expect(canStartAdventure(state)).toBe(false)
    expect(startDisabledReason(state)).toMatch(/approve at least one player/i)
  })

  it('is disabled while the request is in flight', () => {
    expect(canStartAdventure({ ...ready, starting: true })).toBe(false)
  })

  it('is disabled once the session has ended', () => {
    const state = { ...ready, sessionOpen: false }
    expect(canStartAdventure(state)).toBe(false)
    expect(startDisabledReason(state)).toMatch(/ended/i)
  })

  it('is disabled when the campaign already has a group adventure running', () => {
    expect(canStartAdventure({ ...ready, runExists: true })).toBe(false)
  })

  it('reports the session ending before the missing players', () => {
    expect(startDisabledReason({ ...ready, sessionOpen: false, turnOrderCount: 0 })).toMatch(/ended/i)
  })
})

describe('buildJoinLink', () => {
  const lan = (lanUrl: string | null): NetworkInfo => ({
    localUrl: 'http://localhost:5000',
    lanUrl,
    inviteCode: null,
    tunnelUrl: null,
    tunnelActive: false,
  })

  it('swaps localhost for the host computers LAN address and keeps the pages port', () => {
    const link = buildJoinLink('http://localhost:5173', 'abc', lan('http://192.168.1.42:5000'))
    expect(link).toEqual({ url: 'http://192.168.1.42:5173/join/abc', reachableFromLan: true })
  })

  it('works for the desktop app, served from the backend port', () => {
    const link = buildJoinLink('http://localhost:5000', 'abc', lan('http://192.168.1.42:5000'))
    expect(link.url).toBe('http://192.168.1.42:5000/join/abc')
  })

  it('treats 127.0.0.1 and ::1 like localhost', () => {
    expect(buildJoinLink('http://127.0.0.1:5173', 'abc', lan('http://10.0.0.5:5000')).url).toBe('http://10.0.0.5:5173/join/abc')
    expect(buildJoinLink('http://[::1]:5173', 'abc', lan('http://10.0.0.5:5000')).url).toBe('http://10.0.0.5:5173/join/abc')
  })

  it('never uses localhost in the link when a LAN address is known', () => {
    expect(buildJoinLink('http://localhost:5173', 'abc', lan('http://192.168.1.42:5000')).url).not.toContain('localhost')
  })

  it('keeps the address when the host already opened the app by a network address', () => {
    const link = buildJoinLink('http://192.168.1.42:5173', 'abc', lan('http://192.168.1.99:5000'))
    expect(link).toEqual({ url: 'http://192.168.1.42:5173/join/abc', reachableFromLan: true })
  })

  it('flags a link that only works on the host computer when no LAN address was found', () => {
    expect(buildJoinLink('http://localhost:5173', 'abc', lan(null))).toEqual({
      url: 'http://localhost:5173/join/abc',
      reachableFromLan: false,
    })
    expect(buildJoinLink('http://localhost:5173', 'abc', null).reachableFromLan).toBe(false)
  })

  it('does not throw on an unusable LAN address or origin', () => {
    expect(buildJoinLink('http://localhost:5173', 'abc', lan('not a url')).reachableFromLan).toBe(false)
    expect(buildJoinLink('not an origin', 'abc', null).url).toBe('/join/abc')
  })

  it('keeps https', () => {
    expect(buildJoinLink('https://localhost:5001', 'abc', lan('http://192.168.1.42:5000')).url).toBe(
      'https://192.168.1.42:5001/join/abc',
    )
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { AxiosError, type AxiosResponse } from 'axios'
import * as api from '../api'
import type { PlayerAdventureRun } from '../api'
import { actionBoxState, endTurnState, isMyTurn, narrationLines, turnRows, usePlayerRun } from '../playerRun'

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return { ...actual, getAdventureRun: vi.fn(), submitPlayerAction: vi.fn(), endMyTurn: vi.fn() }
})

const mockedGetRun = vi.mocked(api.getAdventureRun)
const mockedSubmit = vi.mocked(api.submitPlayerAction)
const mockedEndTurn = vi.mocked(api.endMyTurn)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

function makeRun(overrides: Partial<PlayerAdventureRun> = {}): PlayerAdventureRun {
  return {
    runId: 'run-1',
    adventureTitle: 'The Cellar Below',
    status: 'InProgress',
    isGroup: true,
    scene: { title: 'The Cellar', description: 'Cold stone steps lead down.' },
    currentSceneNumber: 1,
    totalScenes: 3,
    activeParticipantId: 'alice',
    activeParticipantName: 'Alice',
    currentRound: 2,
    turnOrder: [
      { participantId: 'alice', participantName: 'Alice', isActive: true },
      { participantId: 'bob', participantName: 'Bob', isActive: false },
    ],
    pendingRoll: null,
    recentNarration: [],
    ...overrides,
  }
}

describe('action box gating', () => {
  it('is enabled for the acting player', () => {
    expect(actionBoxState(makeRun(), 'alice', false)).toEqual({ enabled: true, reason: null })
  })

  it('is disabled with "Waiting for <name>" for everyone else', () => {
    expect(actionBoxState(makeRun(), 'bob', false)).toEqual({ enabled: false, reason: 'Waiting for Alice' })
  })

  it('is disabled while the AI DM is answering, even on your own turn', () => {
    const state = actionBoxState(makeRun(), 'alice', true)

    expect(state.enabled).toBe(false)
    expect(state.reason).toContain('answering')
  })

  it('is disabled when no adventure has been started', () => {
    const state = actionBoxState(null, 'alice', false)

    expect(state.enabled).toBe(false)
    expect(state.reason).toContain('not started')
  })

  it('is disabled once the adventure is over', () => {
    expect(actionBoxState(makeRun({ status: 'Completed' }), 'alice', false).enabled).toBe(false)
  })

  it('is disabled for a solo run, which has no turn order', () => {
    const solo = makeRun({ isGroup: false, activeParticipantId: null, activeParticipantName: null, turnOrder: [] })

    expect(actionBoxState(solo, 'alice', false).enabled).toBe(false)
  })

  it('is disabled when the caller is unknown, and falls back to a generic name when the active name is missing', () => {
    expect(actionBoxState(makeRun(), null, false).enabled).toBe(false)
    expect(actionBoxState(makeRun({ activeParticipantName: null }), 'bob', false).reason).toBe('Waiting for the next player')
  })

  it('isMyTurn follows the active participant id', () => {
    expect(isMyTurn(makeRun(), 'alice')).toBe(true)
    expect(isMyTurn(makeRun(), 'bob')).toBe(false)
    expect(isMyTurn(null, 'alice')).toBe(false)
  })
})

describe('turn order and narration', () => {
  it('shows positions from 1: the server\'s turn position is zero-based', () => {
    const rows = turnRows(makeRun(), 'bob')

    expect(rows.map((r) => r.position)).toEqual([1, 2])
    expect(rows.map((r) => r.participantName)).toEqual(['Alice', 'Bob'])
    expect(rows.find((r) => r.isMe)?.participantName).toBe('Bob')
    expect(rows.find((r) => r.isActive)?.participantName).toBe('Alice')
  })

  it('has no rows without a run, and no narration', () => {
    expect(turnRows(null, 'alice')).toEqual([])
    expect(narrationLines(null)).toEqual([])
  })
})

describe('usePlayerRun', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads the run, and treats "no adventure" (null) as not started', async () => {
    mockedGetRun.mockResolvedValueOnce(null)
    const state = usePlayerRun('session-1')

    await state.refresh()

    expect(state.run.value).toBeNull()
    expect(state.loaded.value).toBe(true)
    expect(state.loadError.value).toBeNull()
  })

  it('keeps the last run and reports the problem when a read fails', async () => {
    mockedGetRun.mockResolvedValueOnce(makeRun())
    const state = usePlayerRun('session-1')
    await state.refresh()

    mockedGetRun.mockRejectedValueOnce(httpError(500))
    await state.refresh()

    expect(state.run.value?.runId).toBe('run-1')
    expect(state.loadError.value).toBeTruthy()
  })

  it('marks the run as refused when the server says the caller may not see it', async () => {
    mockedGetRun.mockRejectedValueOnce(httpError(403))
    const state = usePlayerRun('session-1')

    await state.refresh()

    expect(state.refused.value).toBe(true)
    expect(state.loadError.value).toContain('not allowed to see')
  })

  it('submits the trimmed action to the run and reads the run again', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedSubmit.mockResolvedValue({ narration: 'The door creaks.' })
    const state = usePlayerRun('session-1')
    await state.refresh()
    mockedGetRun.mockClear()

    const ok = await state.submit('  I open the door.  ')

    expect(ok).toBe(true)
    expect(mockedSubmit).toHaveBeenCalledWith('run-1', 'I open the door.')
    expect(mockedGetRun).toHaveBeenCalledTimes(1)
    expect(state.submitting.value).toBe(false)
    expect(state.actionError.value).toBeNull()
  })

  it('shows a readable message for "not your turn" and still reads the run again', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedSubmit.mockRejectedValue(httpError(403, { title: 'Not your turn', detail: 'It is not your turn. Waiting for Alice.' }))
    const state = usePlayerRun('session-1')
    await state.refresh()
    mockedGetRun.mockClear()

    const ok = await state.submit('I attack')

    expect(ok).toBe(false)
    expect(state.actionError.value).toBe('It is not your turn. Waiting for Alice.')
    expect(mockedGetRun).toHaveBeenCalledTimes(1)
  })

  it('shows a readable message when the AI provider fails (409 and 500)', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    const state = usePlayerRun('session-1')
    await state.refresh()

    mockedSubmit.mockRejectedValueOnce(httpError(409, { detail: 'Another action is already being resolved.' }))
    await state.submit('first')
    expect(state.actionError.value).toBe('Another action is already being resolved.')

    mockedSubmit.mockRejectedValueOnce(httpError(500))
    await state.submit('second')
    expect(state.actionError.value).toContain('AI provider failed')
  })

  it('does not send an empty action, or send twice while one is in flight', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    let finish: (value: { narration: string }) => void = () => undefined
    mockedSubmit.mockReturnValue(new Promise((resolve) => (finish = resolve)))
    const state = usePlayerRun('session-1')
    await state.refresh()

    expect(await state.submit('   ')).toBe(false)

    const first = state.submit('I search.')
    expect(state.submitting.value).toBe(true)
    expect(await state.submit('I search again.')).toBe(false)
    finish({ narration: 'Nothing.' })
    await first

    expect(mockedSubmit).toHaveBeenCalledTimes(1)
  })

  it('does nothing when there is no run to act in', async () => {
    mockedGetRun.mockResolvedValue(null)
    const state = usePlayerRun('session-1')
    await state.refresh()

    expect(await state.submit('I wave.')).toBe(false)
    expect(mockedSubmit).not.toHaveBeenCalled()
  })

  it('lets the newest read win when two overlap', async () => {
    const resolvers: Array<(run: PlayerAdventureRun) => void> = []
    mockedGetRun.mockImplementation(() => new Promise((resolve) => resolvers.push(resolve)))
    const state = usePlayerRun('session-1')

    const older = state.refresh()
    const newer = state.refresh()
    resolvers[1](makeRun({ currentRound: 5 }))
    await newer
    resolvers[0](makeRun({ currentRound: 1 }))
    await older

    expect(state.run.value?.currentRound).toBe(5)
  })
})

describe('the "End my turn" control', () => {
  const idle = { submitting: false, ending: false }

  it('is an enabled button for the acting player', () => {
    expect(endTurnState(makeRun(), 'alice', idle)).toEqual({ kind: 'button', enabled: true, reason: null })
  })

  it('is a disabled button saying "Waiting for <name>" for everyone else', () => {
    expect(endTurnState(makeRun(), 'bob', idle)).toEqual({ kind: 'button', enabled: false, reason: 'Waiting for Alice' })
  })

  it('falls back to a generic name when the acting player has none', () => {
    expect(endTurnState(makeRun({ activeParticipantName: null }), 'bob', idle).reason).toBe('Waiting for the next player')
  })

  it("is disabled while the AI answers the player's action, and while ending the turn is in flight", () => {
    expect(endTurnState(makeRun(), 'alice', { submitting: true, ending: false }).enabled).toBe(false)
    expect(endTurnState(makeRun(), 'alice', { submitting: false, ending: true })).toMatchObject({ enabled: false, reason: 'Ending your turn…' })
  })

  it('is replaced by a note when the run moves the turn on by itself', () => {
    const state = endTurnState(makeRun({ turnAdvance: 'AfterEachAction' }), 'alice', idle)

    expect(state.kind).toBe('automatic')
    expect(state.enabled).toBe(false)
    expect(state.reason).toContain('by itself')
  })

  it('shows the note to everyone at the table, not only the acting player', () => {
    expect(endTurnState(makeRun({ turnAdvance: 'AfterEachAction' }), 'bob', idle).kind).toBe('automatic')
  })

  it('is hidden with no adventure, a solo run, or a finished one', () => {
    expect(endTurnState(null, 'alice', idle).kind).toBe('hidden')
    expect(endTurnState(makeRun({ isGroup: false, turnOrder: [], activeParticipantId: null }), 'alice', idle).kind).toBe('hidden')
    expect(endTurnState(makeRun({ status: 'Completed' }), 'alice', idle).kind).toBe('hidden')
  })

  it('treats a run from a server without the setting as manual', () => {
    expect(endTurnState(makeRun({ turnAdvance: undefined }), 'alice', idle).kind).toBe('button')
  })
})

describe('usePlayerRun.endTurn', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('ends the turn and takes the table view the server returns as the new state', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedEndTurn.mockResolvedValue(makeRun({ activeParticipantId: 'bob', activeParticipantName: 'Bob' }))
    const state = usePlayerRun('session-1')
    await state.refresh()

    const moved = await state.endTurn()

    expect(moved).toBe(true)
    expect(mockedEndTurn).toHaveBeenCalledWith('run-1')
    expect(state.run.value?.activeParticipantId).toBe('bob')
    expect(state.endTurnError.value).toBeNull()
    expect(state.endingTurn.value).toBe(false)
  })

  it('reads the run again when the server had nothing to return (204)', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedEndTurn.mockResolvedValue(null)
    const state = usePlayerRun('session-1')
    await state.refresh()
    mockedGetRun.mockClear()
    mockedGetRun.mockResolvedValue(makeRun({ activeParticipantId: 'bob' }))

    expect(await state.endTurn()).toBe(true)

    expect(mockedGetRun).toHaveBeenCalledTimes(1)
    expect(state.run.value?.activeParticipantId).toBe('bob')
  })

  it("shows the server's \"not your turn\" message and reads the run again, when the turn had already moved on", async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedEndTurn.mockRejectedValue(httpError(403, { title: 'Not your turn', detail: 'It is not your turn. Waiting for Bob.' }))
    const state = usePlayerRun('session-1')
    await state.refresh()
    mockedGetRun.mockClear()
    mockedGetRun.mockResolvedValue(makeRun({ activeParticipantId: 'bob', activeParticipantName: 'Bob' }))

    const moved = await state.endTurn()

    expect(moved).toBe(false)
    expect(state.endTurnError.value).toBe('It is not your turn. Waiting for Bob.')
    expect(mockedGetRun).toHaveBeenCalledTimes(1)
    expect(state.run.value?.activeParticipantName).toBe('Bob')   // the screen now shows who has it
  })

  it('does not send twice while one request is in flight, or with no run', async () => {
    let finish: (run: PlayerAdventureRun) => void = () => undefined
    mockedGetRun.mockResolvedValue(makeRun())
    mockedEndTurn.mockReturnValue(new Promise((resolve) => (finish = resolve)))
    const state = usePlayerRun('session-1')
    expect(await state.endTurn()).toBe(false)   // no run yet
    await state.refresh()

    const first = state.endTurn()
    expect(state.endingTurn.value).toBe(true)
    expect(await state.endTurn()).toBe(false)
    finish(makeRun({ activeParticipantId: 'bob' }))
    await first

    expect(mockedEndTurn).toHaveBeenCalledTimes(1)
  })

  it('clears an earlier error when the next attempt works', async () => {
    mockedGetRun.mockResolvedValue(makeRun())
    mockedEndTurn.mockRejectedValueOnce(httpError(500)).mockResolvedValueOnce(makeRun({ activeParticipantId: 'bob' }))
    const state = usePlayerRun('session-1')
    await state.refresh()

    await state.endTurn()
    expect(state.endTurnError.value).toBeTruthy()
    await state.endTurn()

    expect(state.endTurnError.value).toBeNull()
  })
})

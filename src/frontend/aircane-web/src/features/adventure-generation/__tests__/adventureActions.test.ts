import { describe, it, expect, vi, beforeEach } from 'vitest'
import { conflictOf } from '../api'
import { attachWithConfirmation, describeConflict, describeDelete, messageOf } from '../adventureActions'

vi.mock('../api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api')>()),
  assignAdventureToCampaign: vi.fn(),
}))

import * as api from '../api'

const conflict = (status: number, data: Record<string, unknown>) =>
  Object.assign(new Error('x'), { response: { status, data } })

const ADVENTURE = { id: 'a1', title: 'The Sunken Archive' }

beforeEach(() => {
  vi.clearAllMocks()
})

describe('conflictOf', () => {
  it('reads a 409 with a known code', () => {
    const c = conflictOf(conflict(409, { code: 'game-system-mismatch', detail: 'd', adventureGameSystem: 'A', campaignGameSystem: 'B' }))
    expect(c).toMatchObject({ code: 'game-system-mismatch', detail: 'd', adventureGameSystem: 'A', campaignGameSystem: 'B' })
  })

  it('is null for other statuses, unknown codes and non-HTTP errors', () => {
    expect(conflictOf(conflict(404, { code: 'game-system-mismatch' }))).toBeNull()
    expect(conflictOf(conflict(409, { code: 'something-else' }))).toBeNull()
    expect(conflictOf(conflict(409, {}))).toBeNull()
    expect(conflictOf(new Error('network'))).toBeNull()
    expect(conflictOf(null)).toBeNull()
  })
})

describe('messageOf', () => {
  it("prefers the server's detail, then the error message, then the fallback", () => {
    expect(messageOf(conflict(409, { detail: 'Server says' }), 'fallback')).toBe('Server says')
    expect(messageOf(new Error('Network Error'), 'fallback')).toBe('Network Error')
    expect(messageOf('weird', 'fallback')).toBe('fallback')
  })
})

describe('describeConflict', () => {
  it('names the adventure, the campaign it is in and the one it is going to', () => {
    const text = describeConflict(
      { code: 'adventure-in-other-campaign', detail: '', currentCampaignName: 'Tuesday Group' }, 'The Sunken Archive', 'Friday Night',
    )
    expect(text).toContain('The Sunken Archive')
    expect(text).toContain('Tuesday Group')
    expect(text).toContain('Friday Night')
  })

  it('names both game systems', () => {
    const text = describeConflict(
      { code: 'game-system-mismatch', detail: '', adventureGameSystem: 'Pathfinder 2e', campaignGameSystem: 'D&D 5e 2014' },
      'The Sunken Archive', 'Friday Night',
    )
    expect(text).toContain('Pathfinder 2e')
    expect(text).toContain('D&D 5e 2014')
    expect(text).toContain('Friday Night')
  })
})

describe('attachWithConfirmation', () => {
  it('sends once when nothing needs agreeing', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockResolvedValue({ id: 'a1' } as never)
    const ask = vi.fn(() => true)

    await attachWithConfirmation(ADVENTURE, 'c1', 'Friday Night', ask)

    expect(api.assignAdventureToCampaign).toHaveBeenCalledOnce()
    expect(api.assignAdventureToCampaign).toHaveBeenCalledWith('a1', 'c1', {})
    expect(ask).not.toHaveBeenCalled()
  })

  it('returns null and sends nothing more when the host declines', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockRejectedValue(conflict(409, { code: 'game-system-mismatch' }))

    const result = await attachWithConfirmation(ADVENTURE, 'c1', 'Friday Night', () => false)

    expect(result).toBeNull()
    expect(api.assignAdventureToCampaign).toHaveBeenCalledOnce()
  })

  it('throws a run-in-progress refusal without asking', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockRejectedValue(conflict(409, { code: 'adventure-run-in-progress', detail: 'busy' }))
    const ask = vi.fn(() => true)

    await expect(attachWithConfirmation(ADVENTURE, 'c1', 'Friday Night', ask)).rejects.toThrow()
    expect(ask).not.toHaveBeenCalled()
  })

  it('throws anything else', async () => {
    vi.mocked(api.assignAdventureToCampaign).mockRejectedValue(conflict(404, { detail: 'Campaign not found' }))
    await expect(attachWithConfirmation(ADVENTURE, 'c1', 'x', () => true)).rejects.toThrow()
  })
})

describe('describeDelete', () => {
  const impact = {
    id: 'a1', title: 'The Sunken Archive', totalRuns: 0, notStartedRuns: 0, inProgressRuns: 0,
    completedRuns: 0, abandonedRuns: 0, transcriptEntries: 0, indexedChunks: 0,
  }

  it('names the adventure and says it cannot be undone', () => {
    const text = describeDelete(impact)
    expect(text).toContain('"The Sunken Archive"')
    expect(text).toContain('cannot be undone')
    expect(text).not.toContain('run')
  })

  it('counts runs by status, transcript entries and indexed pieces, with the right plurals', () => {
    const text = describeDelete({ ...impact, totalRuns: 2, completedRuns: 1, abandonedRuns: 1, transcriptEntries: 1, indexedChunks: 1 })
    expect(text).toContain('2 runs (1 completed, 1 abandoned)')
    expect(text).toContain('1 transcript entry')
    expect(text).toContain('1 piece)')
  })
})

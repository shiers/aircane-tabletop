import { describe, it, expect, vi, beforeEach } from 'vitest'

const get = vi.hoisted(() => vi.fn())
vi.mock('@/shared/api/client', () => ({ default: { get, post: vi.fn() } }))

import { getRulesLookupRulesets } from '../api'

describe('getRulesLookupRulesets', () => {
  beforeEach(() => get.mockReset())

  it('passes the game system as a query param', async () => {
    get.mockResolvedValue({ data: ['2014', '2024'] })
    const result = await getRulesLookupRulesets('D&D 5e')
    expect(get).toHaveBeenCalledWith('/api/ai/rules-question/rulesets', {
      params: { gameSystem: 'D&D 5e' },
    })
    expect(result).toEqual(['2014', '2024'])
  })

  it('omits params when no game system is selected', async () => {
    get.mockResolvedValue({ data: [] })
    await getRulesLookupRulesets(null)
    expect(get).toHaveBeenCalledWith('/api/ai/rules-question/rulesets', { params: undefined })
  })
})

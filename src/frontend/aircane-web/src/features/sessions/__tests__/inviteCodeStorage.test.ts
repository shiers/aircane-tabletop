import { describe, it, expect, beforeEach, vi } from 'vitest'
import {
  MAX_AGE_MS,
  clearInviteCode,
  loadInviteCode,
  saveInviteCode,
  sweepExpiredInviteCodes,
} from '../inviteCodeStorage'

const NOW = 1_700_000_000_000

describe('inviteCodeStorage', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('keeps a code across a "reload" (a fresh read)', () => {
    saveInviteCode('s1', 'ABCD1234', NOW)

    expect(loadInviteCode('s1', NOW + 60_000)).toBe('ABCD1234')
  })

  it('is per session', () => {
    saveInviteCode('s1', 'AAAA1111', NOW)
    saveInviteCode('s2', 'BBBB2222', NOW)

    expect(loadInviteCode('s1', NOW)).toBe('AAAA1111')
    expect(loadInviteCode('s2', NOW)).toBe('BBBB2222')
    expect(loadInviteCode('s3', NOW)).toBeNull()
  })

  it('forgets a code once the session has ended', () => {
    saveInviteCode('s1', 'ABCD1234', NOW)

    clearInviteCode('s1')

    expect(loadInviteCode('s1', NOW)).toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('ignores and removes a code older than the maximum age', () => {
    saveInviteCode('s1', 'ABCD1234', NOW)

    expect(loadInviteCode('s1', NOW + MAX_AGE_MS + 1)).toBeNull()
    expect(localStorage.length).toBe(0)
  })

  it('still returns a code right up to the maximum age', () => {
    saveInviteCode('s1', 'ABCD1234', NOW)

    expect(loadInviteCode('s1', NOW + MAX_AGE_MS)).toBe('ABCD1234')
  })

  it('sweeps expired codes when saving a new one, and keeps the others', () => {
    saveInviteCode('old', 'OLD00000', NOW - MAX_AGE_MS - 10)
    saveInviteCode('recent', 'NEW11111', NOW - 1000)

    saveInviteCode('fresh', 'FRESH222', NOW)

    expect(localStorage.getItem('aircane.inviteCode.old')).toBeNull()
    expect(loadInviteCode('recent', NOW)).toBe('NEW11111')
    expect(loadInviteCode('fresh', NOW)).toBe('FRESH222')
  })

  it('sweeps unreadable entries but leaves unrelated keys alone', () => {
    localStorage.setItem('aircane.inviteCode.broken', '{not json')
    localStorage.setItem('participant_token', 'keep-me')

    sweepExpiredInviteCodes(NOW)

    expect(localStorage.getItem('aircane.inviteCode.broken')).toBeNull()
    expect(localStorage.getItem('participant_token')).toBe('keep-me')
  })

  it('treats a malformed entry as no code', () => {
    localStorage.setItem('aircane.inviteCode.s1', '{"code":42}')

    expect(loadInviteCode('s1', NOW)).toBeNull()
  })

  it('does not save an empty code or an empty session id', () => {
    saveInviteCode('s1', '', NOW)
    saveInviteCode('', 'ABCD1234', NOW)

    expect(localStorage.length).toBe(0)
  })

  it('works without storage: nothing throws and nothing is returned', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(() => saveInviteCode('s1', 'ABCD1234', NOW)).not.toThrow()
    expect(loadInviteCode('s1', NOW)).toBeNull()
    expect(() => clearInviteCode('s1')).not.toThrow()
  })
})

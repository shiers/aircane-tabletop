import { describe, it, expect, beforeEach } from 'vitest'
import {
  MAX_REJOIN_SECRETS,
  forgetRejoinSecret,
  getOrCreateRejoinSecret,
  getRejoinSecret,
  listRejoinSecrets,
  newRejoinSecret,
  rememberRejoinName,
} from '../rejoinSecrets'

describe('rejoin secrets', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
  })

  it('makes 128 random bits as base64url, different every time', () => {
    const a = newRejoinSecret()
    const b = newRejoinSecret()

    expect(a).toMatch(/^[A-Za-z0-9_-]{22}$/)   // 16 bytes, no padding
    expect(a).not.toBe(b)
  })

  it('makes a secret once per session and keeps it in localStorage, not the tab', () => {
    const first = getOrCreateRejoinSecret('session-1', 'Bifur')
    const again = getOrCreateRejoinSecret('session-1', 'Someone else')
    const other = getOrCreateRejoinSecret('session-2', 'Bifur')

    expect(again).toBe(first)
    expect(other).not.toBe(first)
    expect(getRejoinSecret('session-1')).toBe(first)
    expect(sessionStorage.length).toBe(0)
    expect(localStorage.getItem('aircane_rejoin_secrets')).toContain(first)
  })

  it('lists the sessions newest first, with the name the browser joined as', () => {
    getOrCreateRejoinSecret('older', 'Nori', 1_000)
    getOrCreateRejoinSecret('newer', 'Bifur', 2_000)
    rememberRejoinName('older', 'Nori the Wise')

    expect(listRejoinSecrets().map((e) => [e.sessionId, e.displayName])).toEqual([
      ['newer', 'Bifur'],
      ['older', 'Nori the Wise'],
    ])
  })

  it(`keeps at most ${MAX_REJOIN_SECRETS} sessions, dropping the oldest`, () => {
    for (let i = 0; i < MAX_REJOIN_SECRETS + 3; i++) getOrCreateRejoinSecret(`session-${i}`, 'Bifur', i)

    const listed = listRejoinSecrets()
    expect(listed).toHaveLength(MAX_REJOIN_SECRETS)
    expect(listed[0].sessionId).toBe(`session-${MAX_REJOIN_SECRETS + 2}`)
    expect(getRejoinSecret('session-0')).toBeNull()
  })

  it('forgets one session without touching the others', () => {
    getOrCreateRejoinSecret('session-1', 'Bifur')
    const kept = getOrCreateRejoinSecret('session-2', 'Bifur')

    forgetRejoinSecret('session-1')

    expect(getRejoinSecret('session-1')).toBeNull()
    expect(getRejoinSecret('session-2')).toBe(kept)
  })

  it('survives a corrupt store: nothing to rejoin with, and a new secret can still be made', () => {
    localStorage.setItem('aircane_rejoin_secrets', '{not json')

    expect(listRejoinSecrets()).toEqual([])
    expect(getOrCreateRejoinSecret('session-1', 'Bifur')).toMatch(/^[A-Za-z0-9_-]{22}$/)
  })
})

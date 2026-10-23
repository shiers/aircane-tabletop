import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import JoinSessionView from '../JoinSessionView.vue'
import RejoinCards from '../components/RejoinCards.vue'
import * as sessionsApi from '../api'
import { loadParticipantSession, saveParticipantSession } from '../participantSession'
import { getOrCreateRejoinSecret, getRejoinSecret } from '../rejoinSecrets'
import {
  REMOVED_MESSAGE,
  isParticipantDenied,
  isParticipantRemoved,
  mapActionError,
  mapEndTurnError,
  mapRejoinError,
  rejoinFailure,
} from '../playerErrors'
import { denyConfirmation, removeConfirmation } from '../hostActions'
import vBgAsset from '@/directives/vBgAsset'

/**
 * A player the host removed: what they are told, that the browser forgets the login and the rejoin secret, that
 * the sessions page does not offer to go back, and that a fresh join is still possible.
 */

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    getJoinInfo: vi.fn(),
    joinSession: vi.fn(),
    reconnectSession: vi.fn(),
    rejoinSession: vi.fn(),
    getRejoinStatus: vi.fn(),
  }
})

const route = vi.hoisted(() => ({ params: { sessionId: 'session-abc' } as Record<string, string>, query: {} as Record<string, string> }))
const router = vi.hoisted(() => ({ replace: vi.fn(), push: vi.fn() }))
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRoute: () => route, useRouter: () => router }
})

const mockedRejoin = vi.mocked(sessionsApi.rejoinSession)
const mockedStatus = vi.mocked(sessionsApi.getRejoinStatus)
const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)
const mockedJoinInfo = vi.mocked(sessionsApi.getJoinInfo)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

const removed = () =>
  httpError(403, {
    title: 'You were removed from this session',
    detail: 'The host removed you from this session. You can ask to join again with the invite code.',
    code: 'participant-removed',
  })

const mounted: Array<{ unmount: () => void }> = []

async function mountJoin() {
  const wrapper = mount(JoinSessionView, {
    global: { plugins: [createPinia()], directives: { 'bg-asset': vBgAsset }, stubs: { RouterLink: true } },
  })
  mounted.push(wrapper)
  await flushPromises()
  return wrapper
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.clearAllMocks()
  sessionStorage.clear()
  localStorage.clear()
  route.params = { sessionId: 'session-abc' }
  route.query = {}
  mockedJoinInfo.mockResolvedValue({ name: 'Friday Table', joinable: true })
})

afterEach(() => {
  mounted.splice(0).forEach((w) => w.unmount())
})

describe('the join page after a removal', () => {
  it('says "The host removed you from this session", above the join form', async () => {
    route.query = { removed: '1' }

    const wrapper = await mountJoin()

    expect(wrapper.find('[data-testid="rejoin-notice"]').text()).toContain('The host removed you from this session')
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
  })

  it('a removed browser trying its rejoin secret is told so, the secret is forgotten, and the form is shown', async () => {
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedRejoin.mockRejectedValue(removed())

    const wrapper = await mountJoin()

    expect(wrapper.find('[data-testid="rejoin-notice"]').text()).toBe(REMOVED_MESSAGE)
    expect(getRejoinSecret('session-abc')).toBeNull()
    expect(loadParticipantSession()).toBeNull()
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
  })

  it('/sessions with a removed login forgets it and the secret, and opens that session\'s page saying why', async () => {
    route.params = {}
    saveParticipantSession({ sessionId: 'session-abc', participantId: 'p-1', displayName: 'Bifur', token: 'revoked' })
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    mockedReconnect.mockRejectedValue(removed())

    await mountJoin()

    expect(loadParticipantSession()).toBeNull()
    expect(getRejoinSecret('session-abc')).toBeNull()
    expect(router.replace).toHaveBeenCalledWith({
      name: 'join-session', params: { sessionId: 'session-abc' }, query: { removed: '1' },
    })
  })
})

describe('the sessions page does not offer a session the player was removed from', () => {
  it('drops the card, and the secret, when the status check says removed', async () => {
    getOrCreateRejoinSecret('session-abc', 'Bifur')
    getOrCreateRejoinSecret('session-xyz', 'Bifur')
    mockedStatus.mockImplementation(async (sessionId: string) => {
      if (sessionId === 'session-abc') throw removed()
      return { sessionName: 'Other Table', displayName: 'Bifur', status: 'approved' }
    })

    const wrapper = mount(RejoinCards)
    await flushPromises()

    const cards = wrapper.findAll('[data-testid="rejoin-card"]')
    expect(cards).toHaveLength(1)
    expect(cards[0].text()).toContain('Other Table')
    expect(getRejoinSecret('session-abc')).toBeNull()
    expect(getRejoinSecret('session-xyz')).not.toBeNull()
  })
})

describe('playerErrors: the removed refusal', () => {
  it('is recognised by its code, and is not a denial', () => {
    expect(isParticipantRemoved(removed())).toBe(true)
    expect(isParticipantDenied(removed())).toBe(false)
    expect(isParticipantRemoved(httpError(403, { code: 'participant-denied' }))).toBe(false)
    expect(isParticipantRemoved(httpError(403))).toBe(false)
  })

  it('is its own rejoin failure, with its own message', () => {
    expect(rejoinFailure(removed())).toBe('removed')
    expect(mapRejoinError(removed())).toBe(REMOVED_MESSAGE)
  })

  it('an action or ending a turn after removal says the host removed them', () => {
    expect(mapActionError(removed())).toBe(REMOVED_MESSAGE)
    expect(mapEndTurnError(removed())).toBe(REMOVED_MESSAGE)
  })
})

describe('hostActions: what the host is asked', () => {
  it('a denial names the person and says they can ask again', () => {
    const text = denyConfirmation('Walt')
    expect(text).toContain("Decline Walt's request to join?")
    expect(text).toContain('invite code')
  })

  it('removing the acting player says the turn moves on, and to whom', () => {
    const text = removeConfirmation('Alice', { inRun: true, holdsTurn: true, nextName: 'Bob', characterName: 'Mira' })
    expect(text).toContain('Remove Alice from this session?')
    expect(text).toContain('Alice loses their seat and is disconnected.')
    expect(text).toContain('the turn moves on to Bob')
    expect(text).toContain('Their character, Mira, is unassigned.')
  })

  it('removing the last player says the adventure waits for a new one', () => {
    const text = removeConfirmation('Alice', { inRun: true, holdsTurn: true, nextName: null, characterName: null })
    expect(text).toContain('nobody else is in the turn order')
    expect(text).toContain('Any character assigned to them is unassigned.')
  })

  it('removing someone outside the adventure says nothing about turns', () => {
    const text = removeConfirmation('Wanda', { inRun: false, holdsTurn: false, nextName: null, characterName: null })
    expect(text).not.toMatch(/turn/i)
  })
})

describe('removeConfirmation with the Characters feature off', () => {
  it('says nothing about characters, whether or not one is assigned', () => {
    for (const characterName of ['Mira', null]) {
      const text = removeConfirmation('Alice', { inRun: true, holdsTurn: true, nextName: 'Bob', characterName, charactersEnabled: false })

      expect(text).toContain('Remove Alice from this session?')
      expect(text).toContain('the turn moves on to Bob')
      expect(text).not.toMatch(/character/i)
    }
  })

  it('still names the character when the feature is on, or when the caller does not say', () => {
    const on = removeConfirmation('Alice', { inRun: false, holdsTurn: false, nextName: null, characterName: 'Mira', charactersEnabled: true })
    const unsaid = removeConfirmation('Alice', { inRun: false, holdsTurn: false, nextName: null, characterName: 'Mira' })

    expect(on).toContain('Their character, Mira, is unassigned.')
    expect(unsaid).toContain('Their character, Mira, is unassigned.')
  })
})

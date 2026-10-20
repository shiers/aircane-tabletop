import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { AxiosError, type AxiosResponse } from 'axios'
import RejoinCards from '../components/RejoinCards.vue'
import JoinSessionView from '../JoinSessionView.vue'
import * as sessionsApi from '../api'
import { loadParticipantSession, saveParticipantSession } from '../participantSession'
import { getOrCreateRejoinSecret, getRejoinSecret } from '../rejoinSecrets'
import { DENIED_MESSAGE } from '../playerErrors'
import vBgAsset from '@/directives/vBgAsset'

/**
 * The sessions page (the sidebar's "Join Session") lists the sessions this browser can go back to. The status
 * check on load logs nobody in; one click on "Rejoin" does.
 */

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    getRejoinStatus: vi.fn(),
    rejoinSession: vi.fn(),
    reconnectSession: vi.fn(),
    getJoinInfo: vi.fn(),
    joinSession: vi.fn(),
  }
})

const router = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn() }))
vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, useRoute: () => ({ params: {}, query: {} }), useRouter: () => router }
})

const mockedStatus = vi.mocked(sessionsApi.getRejoinStatus)
const mockedRejoin = vi.mocked(sessionsApi.rejoinSession)
const mockedReconnect = vi.mocked(sessionsApi.reconnectSession)

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

const seat = (isApproved: boolean): sessionsApi.RejoinSessionResult => ({
  participantId: 'participant-1',
  displayName: 'Bifur',
  role: sessionsApi.ParticipantRole.Player,
  isApproved,
  status: isApproved ? 'approved' : 'pending',
  characterId: null,
  sessionName: 'Friday Table',
  participantToken: 'fresh-token',
})

async function mountCards() {
  const wrapper = mount(RejoinCards)
  await flushPromises()
  return wrapper
}

const cards = (w: ReturnType<typeof mount>) => w.findAll('[data-testid="rejoin-card"]')

describe('RejoinCards', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  it('shows nothing when the browser has no session to go back to', async () => {
    const wrapper = await mountCards()

    expect(wrapper.find('[data-testid="rejoin-cards"]').exists()).toBe(false)
    expect(mockedStatus).not.toHaveBeenCalled()
  })

  it('shows a card per session with its name, the player name and the status, without logging in', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur', 2)
    getOrCreateRejoinSecret('sunday', 'Nori', 1)
    mockedStatus.mockImplementation(async (sessionId) =>
      sessionId === 'friday'
        ? { sessionName: 'Friday Table', displayName: 'Bifur', status: 'approved' }
        : { sessionName: 'Sunday Game', displayName: 'Nori', status: 'pending' })

    const wrapper = await mountCards()

    expect(cards(wrapper)).toHaveLength(2)
    expect(cards(wrapper)[0].find('[data-testid="rejoin-card-session"]').text()).toBe('Friday Table')
    expect(cards(wrapper)[0].find('[data-testid="rejoin-card-name"]').text()).toBe('Bifur')
    expect(cards(wrapper)[0].find('[data-testid="rejoin-card-status"]').text()).toBe('In the session')
    expect(cards(wrapper)[1].find('[data-testid="rejoin-card-status"]').text()).toBe('Waiting for the host to approve you')
    expect(mockedStatus).toHaveBeenCalledWith('friday', getRejoinSecret('friday'))
    expect(mockedRejoin).not.toHaveBeenCalled()
    expect(loadParticipantSession()).toBeNull()   // the status check minted no login
  })

  it('silently removes sessions that ended, are unknown, or where the host declined', async () => {
    getOrCreateRejoinSecret('ended', 'Bifur', 3)
    getOrCreateRejoinSecret('declined', 'Bifur', 2)
    getOrCreateRejoinSecret('kept', 'Bifur', 1)
    mockedStatus.mockImplementation(async (sessionId) => {
      if (sessionId === 'ended') throw httpError(404, { title: 'No place to return to' })
      if (sessionId === 'declined') return { sessionName: 'X', displayName: 'Bifur', status: 'denied' }
      return { sessionName: 'Kept', displayName: 'Bifur', status: 'approved' }
    })

    const wrapper = await mountCards()

    expect(cards(wrapper)).toHaveLength(1)
    expect(wrapper.text()).not.toContain('No place to return to')
    expect(getRejoinSecret('ended')).toBeNull()
    expect(getRejoinSecret('declined')).toBeNull()
    expect(getRejoinSecret('kept')).not.toBeNull()
  })

  it('says clearly when the host cannot be reached, and keeps the card', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedStatus.mockRejectedValue(new AxiosError('Network Error', 'ERR_NETWORK'))

    const wrapper = await mountCards()

    expect(cards(wrapper)).toHaveLength(1)
    expect(wrapper.find('[data-testid="rejoin-card-name"]').text()).toBe('Bifur')   // from what the browser remembered
    expect(wrapper.find('[data-testid="rejoin-card-problem"]').text()).toContain("Can't reach the host")
    expect(getRejoinSecret('friday')).not.toBeNull()
  })

  it('one click takes an approved seat back and goes to the session', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedStatus.mockResolvedValue({ sessionName: 'Friday Table', displayName: 'Bifur', status: 'approved' })
    mockedRejoin.mockResolvedValue(seat(true))
    const wrapper = await mountCards()

    await wrapper.find('[data-testid="rejoin-button"]').trigger('click')
    await flushPromises()

    expect(mockedRejoin).toHaveBeenCalledWith('friday', getRejoinSecret('friday'))
    expect(loadParticipantSession()).toMatchObject({ sessionId: 'friday', participantId: 'participant-1', token: 'fresh-token' })
    expect(router.push).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'friday' } })
  })

  it('one click on a pending seat goes to the waiting screen', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedStatus.mockResolvedValue({ sessionName: 'Friday Table', displayName: 'Bifur', status: 'pending' })
    mockedRejoin.mockResolvedValue(seat(false))
    const wrapper = await mountCards()

    await wrapper.find('[data-testid="rejoin-button"]').trigger('click')
    await flushPromises()

    expect(router.push).toHaveBeenCalledWith({ name: 'join-session', params: { sessionId: 'friday' }, query: { returning: '1' } })
  })

  it('says so on the card when the host declined in the meantime, and forgets the secret', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedStatus.mockResolvedValue({ sessionName: 'Friday Table', displayName: 'Bifur', status: 'pending' })
    mockedRejoin.mockRejectedValue(httpError(403, { code: 'participant-denied' }))
    const wrapper = await mountCards()

    await wrapper.find('[data-testid="rejoin-button"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="rejoin-card-problem"]').text()).toBe(DENIED_MESSAGE)
    expect(wrapper.find('[data-testid="rejoin-button"]').exists()).toBe(false)
    expect(getRejoinSecret('friday')).toBeNull()
    expect(router.push).not.toHaveBeenCalled()
  })
})

describe('the sessions page with rejoin cards', () => {
  function tokenExpiring(expSeconds: number): string {
    return `h.${btoa(JSON.stringify({ exp: expSeconds })).replace(/=+$/, '')}.s`
  }

  async function mountIndex() {
    const wrapper = mount(JoinSessionView, {
      global: { plugins: [createPinia()], directives: { 'bg-asset': vBgAsset }, stubs: { RouterLink: true } },
    })
    await flushPromises()
    return wrapper
  }

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    sessionStorage.clear()
  })

  it('shows the cards above the unchanged join form', async () => {
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedStatus.mockResolvedValue({ sessionName: 'Friday Table', displayName: 'Bifur', status: 'approved' })

    const wrapper = await mountIndex()

    const html = wrapper.html()
    expect(wrapper.find('[data-testid="rejoin-cards"]').exists()).toBe(true)
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
    expect(html.indexOf('data-testid="rejoin-cards"')).toBeLessThan(html.indexOf('join-display-name'))
  })

  it('still takes a player with a live login straight back to their session (#56), without checking secrets', async () => {
    saveParticipantSession({
      sessionId: 'friday', participantId: 'participant-1', displayName: 'Bifur',
      token: tokenExpiring(Math.floor(Date.now() / 1000) + 3600),
    })
    getOrCreateRejoinSecret('friday', 'Bifur')
    mockedReconnect.mockResolvedValue({
      sessionId: 'friday', participantId: 'participant-1', displayName: 'Bifur',
      role: sessionsApi.ParticipantRole.Player, isApproved: true,
    })

    await mountIndex()

    expect(router.replace).toHaveBeenCalledWith({ name: 'play-session', params: { sessionId: 'friday' } })
    expect(mockedStatus).not.toHaveBeenCalled()
  })
})

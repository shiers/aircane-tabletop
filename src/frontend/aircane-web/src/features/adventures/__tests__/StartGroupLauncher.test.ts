import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { AxiosError, type AxiosResponse } from 'axios'
import StartGroupLauncher from '../components/StartGroupLauncher.vue'
import { useCampaignStore } from '@/features/campaigns/store'
import { AiRole, AiAuthority, type CampaignDto } from '@/features/campaigns/api'
import { SessionAccessMode, SessionStatus, type SessionDto } from '@/features/sessions/api'
import { loadInviteCode } from '@/features/sessions/inviteCodeStorage'
import * as adventuresApi from '../api'
import * as sessionsApi from '@/features/sessions/api'
import { groupRunState } from './fixtures'

vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api')>()
  return { ...actual, getActiveAdventureSession: vi.fn(), getAdventureSession: vi.fn() }
})

vi.mock('@/features/sessions/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/features/sessions/api')>()
  return { ...actual, startSession: vi.fn() }
})

const startSession = vi.mocked(sessionsApi.startSession)
const getActive = vi.mocked(adventuresApi.getActiveAdventureSession)
const getRun = vi.mocked(adventuresApi.getAdventureSession)

function campaign(overrides: Partial<CampaignDto> = {}): CampaignDto {
  return {
    id: 'campaign-1',
    name: 'Friday Night',
    gameSystem: 'D&D 5e',
    ruleset: '2014',
    gameSystemDefinitionId: null,
    aiRole: AiRole.FullDm,
    aiAuthority: AiAuthority.FullSessionControl,
    activeAdventureId: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    ...overrides,
  }
}

function session(overrides: Partial<SessionDto> = {}): SessionDto {
  return {
    id: 'play-session-1',
    campaignId: 'campaign-1',
    name: 'Group: The Sunken Archive',
    accessMode: SessionAccessMode.LocalLan,
    status: SessionStatus.Active,
    startedAt: null,
    endedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    participantCount: 0,
    inviteCode: 'ABCD1234',
    joinUrl: '/join/play-session-1',
    ...overrides,
  }
}

function httpError(status: number, data?: unknown): AxiosError {
  const response = { status, data, statusText: '', headers: {}, config: {} } as AxiosResponse
  return new AxiosError(`Request failed with status code ${status}`, 'ERR_BAD_REQUEST', undefined, undefined, response)
}

type Props = { adventureId: string; adventureTitle?: string; campaignId?: string | null }

async function mountLauncher(props: Props, campaigns: CampaignDto[] = [campaign()], fetchFails = false) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const campaignStore = useCampaignStore(pinia)
  vi.spyOn(campaignStore, 'fetchCampaigns').mockImplementation(async () => {
    if (fetchFails) throw new Error('boom')
    campaignStore.campaigns = campaigns
  })

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/sessions/:sessionId/host', name: 'host-session', component: { template: '<div />' } },
      { path: '/adventures/solo/:sessionId', name: 'adventure-solo', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()

  const wrapper = mount(StartGroupLauncher, {
    props,
    global: { plugins: [pinia, router], stubs: { RouterLink: RouterLinkStub } },
  })
  return { wrapper, router }
}

/** Opens the panel and presses "Create lobby" (an adventure that has a campaign needs no picking). */
async function createLobby(wrapper: Awaited<ReturnType<typeof mountLauncher>>['wrapper']): Promise<void> {
  await wrapper.find('[data-testid="start-group-button"]').trigger('click')
  await flushPromises()
  await wrapper.find('[data-testid="start-button"]').trigger('click')
  await flushPromises()
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  getActive.mockResolvedValue(null)
})

describe('StartGroupLauncher', () => {
  it('offers "Start Group Session", not Play Solo', async () => {
    const { wrapper } = await mountLauncher({ adventureId: 'adventure-1' })

    const button = wrapper.find('[data-testid="start-group-button"]')
    expect(button.text()).toContain('Start Group Session')
    expect(wrapper.text()).not.toContain('Play Solo')
  })

  describe('adventure with no campaign (asks which one)', () => {
    it('opens a campaign picker and only creates the lobby once a campaign is chosen', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper, router } = await mountLauncher(
        { adventureId: 'adventure-1', adventureTitle: 'The Sunken Archive' },
        [campaign(), campaign({ id: 'campaign-2', name: 'Other' })],
      )

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="start-button"]').attributes('disabled')).toBeDefined()
      expect(startSession).not.toHaveBeenCalled()

      await wrapper.find('[data-testid="campaign-select"]').setValue('campaign-2')
      await wrapper.find('[data-testid="start-button"]').trigger('click')
      await flushPromises()

      expect(startSession).toHaveBeenCalledWith('campaign-2', expect.objectContaining({ accessMode: SessionAccessMode.LocalLan }))
      expect(router.currentRoute.value.fullPath).toBe('/sessions/play-session-1/host?adventure=adventure-1')
    })

    it('preselects the only campaign', async () => {
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1' })

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()

      expect((wrapper.find('[data-testid="campaign-select"]').element as HTMLSelectElement).value).toBe('campaign-1')
    })

    it('with no campaigns, points to creating one', async () => {
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1' }, [])

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="no-campaigns"]').text()).toContain('You need a campaign')
      expect(wrapper.find('[data-testid="campaigns-load-failed"]').exists()).toBe(false)
    })

    it('when the campaigns cannot be loaded, says so instead of "you have no campaigns"', async () => {
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1' }, [], true)

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="campaigns-load-failed"]').text()).toContain('Could not load your campaigns')
      expect(wrapper.find('[data-testid="no-campaigns"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="retry-campaigns"]').exists()).toBe(true)
    })
  })

  describe('adventure already in a campaign', () => {
    it('asks no campaign, only the options, then creates the play session', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper, router } = await mountLauncher({
        adventureId: 'adventure-1',
        adventureTitle: 'The Sunken Archive',
        campaignId: 'campaign-1',
      })

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="campaign-select"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="require-approval"]').exists()).toBe(true)
      expect(startSession).not.toHaveBeenCalled()

      await wrapper.find('[data-testid="start-button"]').trigger('click')
      await flushPromises()

      expect(startSession).toHaveBeenCalledWith('campaign-1', {
        name: 'Group: The Sunken Archive',
        accessMode: SessionAccessMode.LocalLan, // 1
        requireHostApproval: true,
      })
      expect(router.currentRoute.value.name).toBe('host-session')
      expect(router.currentRoute.value.query.adventure).toBe('adventure-1')
    })

    it('keeps the invite code, because the server returns it only once', async () => {
      startSession.mockResolvedValue(session({ inviteCode: 'WXYZ9876' }))
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(loadInviteCode('play-session-1')).toBe('WXYZ9876')
    })

    it('still opens the lobby if the server sent no invite code', async () => {
      startSession.mockResolvedValue(session({ inviteCode: null }))
      const { wrapper, router } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(router.currentRoute.value.name).toBe('host-session')
      expect(loadInviteCode('play-session-1')).toBeNull()
    })

    it('names the session from the adventure title, and has a fallback', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(startSession).toHaveBeenCalledWith('campaign-1', expect.objectContaining({ name: 'Group session' }))
    })

    it('does not end or delete any session', async () => {
      startSession.mockResolvedValue(session())
      const endSession = vi.spyOn(sessionsApi, 'endSession')
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(endSession).not.toHaveBeenCalled()
    })
  })

  describe('a campaign that is already running an adventure', () => {
    it('warns instead of creating a play session, and offers to open it (same adventure)', async () => {
      getActive.mockResolvedValue({ id: 'run-1' } as never)
      getRun.mockResolvedValue(groupRunState({ id: 'run-1', generatedAdventureId: 'adventure-1', sessionId: 'play-9' }))
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(startSession).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="launcher-error"]').text()).toContain('already running this adventure')
      expect(wrapper.find('[data-testid="open-run-button"]').text()).toContain('Open the adventure in progress')
    })

    it('names the other adventure when it is a different one', async () => {
      getActive.mockResolvedValue({ id: 'run-1' } as never)
      getRun.mockResolvedValue(groupRunState({ generatedAdventureId: 'other', adventureTitle: 'The Drowned Bell' }))
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(startSession).not.toHaveBeenCalled()
      expect(wrapper.find('[data-testid="launcher-error"]').text()).toContain('The Drowned Bell')
    })

    it('opens a group run on the host screen', async () => {
      getActive.mockResolvedValue({ id: 'run-1' } as never)
      getRun.mockResolvedValue(groupRunState({ id: 'run-1', generatedAdventureId: 'adventure-1', sessionId: 'play-9' }))
      const { wrapper, router } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })
      await createLobby(wrapper)

      await wrapper.find('[data-testid="open-run-button"]').trigger('click')
      await flushPromises()

      expect(router.currentRoute.value.fullPath).toBe('/sessions/play-9/host')
    })
  })

  describe('errors', () => {
    it('shows the host-only message for a 403', async () => {
      startSession.mockRejectedValue(httpError(403))
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(wrapper.find('[data-testid="launcher-error"]').text()).toMatch(/only the host/i)
    })

    it('shows the server message for a 409', async () => {
      startSession.mockRejectedValue(httpError(409, { detail: 'That conflicts.' }))
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(wrapper.find('[data-testid="launcher-error"]').text()).toBe('That conflicts.')
    })

    it('stays on the page and can try again after a failure', async () => {
      startSession.mockRejectedValueOnce(httpError(500)).mockResolvedValueOnce(session())
      const { wrapper, router } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)
      expect(router.currentRoute.value.path).toBe('/')

      // The panel stays open with the error: press "Create lobby" again.
      await wrapper.find('[data-testid="start-button"]').trigger('click')
      await flushPromises()
      expect(router.currentRoute.value.name).toBe('host-session')
    })
  })

  describe('options', () => {
    it('requires host approval by default, and sends the real value when it is turned off', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()
      const box = wrapper.find('[data-testid="require-approval"]')
      expect((box.element as HTMLInputElement).checked).toBe(true)
      await box.setValue(false)
      await wrapper.find('[data-testid="start-button"]').trigger('click')
      await flushPromises()

      expect(startSession).toHaveBeenCalledWith('campaign-1', expect.objectContaining({ requireHostApproval: false }))
    })

    it('turn advance defaults to manual and carries "after each action" to the lobby', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper, router } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await wrapper.find('[data-testid="start-group-button"]').trigger('click')
      await flushPromises()
      const select = wrapper.find('[data-testid="turn-advance-select"]')
      expect((select.element as HTMLSelectElement).value).toBe('ManualOnly')
      await select.setValue('AfterEachAction')
      await wrapper.find('[data-testid="start-button"]').trigger('click')
      await flushPromises()

      expect(router.currentRoute.value.query).toEqual({ adventure: 'adventure-1', turnAdvance: 'AfterEachAction' })
    })

    it('leaves the default turn advance out of the lobby link', async () => {
      startSession.mockResolvedValue(session())
      const { wrapper, router } = await mountLauncher({ adventureId: 'adventure-1', campaignId: 'campaign-1' })

      await createLobby(wrapper)

      expect(router.currentRoute.value.query).toEqual({ adventure: 'adventure-1' })
    })
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PlayerJoinForm from '../PlayerJoinForm.vue'
import * as sessionsApi from '../api'

// Mock the sessions API so no real HTTP calls are made
vi.mock('../api', async (importOriginal) => {
  const actual = await importOriginal<typeof sessionsApi>()
  return {
    ...actual,
    joinSession: vi.fn(),
  }
})

const mockedJoinSession = vi.mocked(sessionsApi.joinSession)

function mountForm(props = {}) {
  return mount(PlayerJoinForm, {
    props: {
      sessionId: 'session-abc',
      sessionName: 'Friday Night Game',
      ...props,
    },
    global: {
      plugins: [createPinia()],
      stubs: {
        RouterLink: true,
      },
    },
  })
}

describe('PlayerJoinForm', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    // Clear sessionStorage between tests
    sessionStorage.clear()
  })

  it('renders the session name in the description', () => {
    const wrapper = mountForm()
    expect(wrapper.text()).toContain('Friday Night Game')
  })

  it('renders display name and invite code inputs', () => {
    const wrapper = mountForm()
    expect(wrapper.find('#join-display-name').exists()).toBe(true)
    expect(wrapper.find('#join-invite-code').exists()).toBe(true)
  })

  it('disables the submit button when fields are empty', () => {
    const wrapper = mountForm()
    const button = wrapper.find('button[type="submit"]')
    expect(button.attributes('disabled')).toBeDefined()
  })

  it('enables the submit button when both fields are filled', async () => {
    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('Thorin')
    await wrapper.find('#join-invite-code').setValue('ABC123')
    const button = wrapper.find('button[type="submit"]')
    expect(button.attributes('disabled')).toBeUndefined()
  })

  it('shows a validation error when display name is empty on submit', async () => {
    const wrapper = mountForm()
    await wrapper.find('#join-invite-code').setValue('ABC123')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.text()).toContain('Display name is required')
  })

  it('shows a validation error when invite code is empty on submit', async () => {
    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('Thorin')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.text()).toContain('Invite code is required')
  })

  it('shows a validation error when display name exceeds 50 characters', async () => {
    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('A'.repeat(51))
    await wrapper.find('#join-invite-code').setValue('ABC123')
    await wrapper.find('form').trigger('submit')
    expect(wrapper.text()).toContain('50 characters or fewer')
  })

  it('calls joinSession with trimmed values and emits joined on success', async () => {
    const joinResult = {
      participantId: 'p-1',
      displayName: 'Thorin',
      role: 0,
      isApproved: true,
      participantToken: 'token.abc',
    }
    mockedJoinSession.mockResolvedValueOnce(joinResult)

    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('  Thorin  ')
    await wrapper.find('#join-invite-code').setValue('  ABC123  ')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockedJoinSession).toHaveBeenCalledWith('session-abc', {
      displayName: 'Thorin',
      inviteCode: 'ABC123',
    })
    expect(wrapper.emitted('joined')).toBeTruthy()
    expect(wrapper.emitted('joined')![0]).toEqual([joinResult])
  })

  it('stores the participant token in sessionStorage after a successful join', async () => {
    const joinResult = {
      participantId: 'p-1',
      displayName: 'Thorin',
      role: 0,
      isApproved: true,
      participantToken: 'my.signed.token',
    }
    mockedJoinSession.mockResolvedValueOnce(joinResult)

    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('Thorin')
    await wrapper.find('#join-invite-code').setValue('ABC123')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(sessionStorage.getItem('participant_token')).toBe('my.signed.token')
  })

  it('does not emit joined when the API call fails', async () => {
    mockedJoinSession.mockRejectedValueOnce(new Error('Invalid invite code'))

    const wrapper = mountForm()
    await wrapper.find('#join-display-name').setValue('Thorin')
    await wrapper.find('#join-invite-code').setValue('WRONG')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('joined')).toBeFalsy()
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import LanJoinScreen from '../LanJoinScreen.vue'
import { SessionAccessMode, SessionStatus } from '../api'

// Mock qrcode so canvas rendering doesn't fail in jsdom
vi.mock('qrcode', () => ({
  default: {
    toCanvas: vi.fn().mockResolvedValue(undefined),
  },
}))

const mockSession = {
  id: 'session-1',
  campaignId: 'campaign-1',
  name: 'Friday Night Game',
  accessMode: SessionAccessMode.LocalLan,
  status: SessionStatus.Active,
  startedAt: '2024-01-01T10:00:00Z',
  endedAt: null,
  createdAt: '2024-01-01T09:00:00Z',
  participantCount: 3,
  inviteCode: 'ABC123',
  joinUrl: 'http://192.168.1.10:5173/join/session-1',
}

function mountScreen(overrides = {}) {
  return mount(LanJoinScreen, {
    props: {
      session: mockSession,
      joinUrl: 'http://192.168.1.10:5173/join/session-1',
      inviteCode: 'ABC123',
      ...overrides,
    },
    attachTo: document.body,
  })
}

describe('LanJoinScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('displays the session name', () => {
    const wrapper = mountScreen()
    expect(wrapper.text()).toContain('Friday Night Game')
  })

  it('displays the join URL', () => {
    const wrapper = mountScreen()
    expect(wrapper.text()).toContain('http://192.168.1.10:5173/join/session-1')
  })

  it('displays the invite code', () => {
    const wrapper = mountScreen()
    expect(wrapper.text()).toContain('ABC123')
  })

  it('displays the participant count', () => {
    const wrapper = mountScreen()
    expect(wrapper.text()).toContain('3')
    expect(wrapper.text()).toContain('players')
  })

  it('shows "player" (singular) when participant count is 1', () => {
    const wrapper = mountScreen({
      session: { ...mockSession, participantCount: 1 },
    })
    // The participant count line should read "1 player connected" not "1 players connected"
    expect(wrapper.text()).toContain('1 player connected')
    expect(wrapper.text()).not.toContain('1 players connected')
  })

  it('shows Active status for an active session', () => {
    const wrapper = mountScreen()
    expect(wrapper.text()).toContain('Active')
  })

  it('shows Pending status for a pending session', () => {
    const wrapper = mountScreen({
      session: { ...mockSession, status: SessionStatus.Pending },
    })
    expect(wrapper.text()).toContain('Pending')
  })

  it('renders a canvas element for the QR code', () => {
    const wrapper = mountScreen()
    expect(wrapper.find('canvas').exists()).toBe(true)
  })

  it('renders copy buttons for the URL and invite code', () => {
    const wrapper = mountScreen()
    const buttons = wrapper.findAll('button')
    // Should have at least 2 copy buttons
    expect(buttons.length).toBeGreaterThanOrEqual(2)
    const buttonTexts = buttons.map((b) => b.text())
    expect(buttonTexts.some((t) => t.includes('Copy'))).toBe(true)
  })

  it('renders the QR code canvas with an accessible aria-label', () => {
    const wrapper = mountScreen()
    const canvas = wrapper.find('canvas')
    expect(canvas.attributes('aria-label')).toBeTruthy()
  })
})

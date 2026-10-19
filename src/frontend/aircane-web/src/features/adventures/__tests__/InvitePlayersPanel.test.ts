import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import InvitePlayersPanel from '../components/InvitePlayersPanel.vue'
import { ParticipantRole, type ParticipantDto } from '@/features/sessions/api'
import type { JoinLink } from '@/features/sessions/lobby'

vi.mock('qrcode', () => ({ default: { toCanvas: vi.fn().mockResolvedValue(undefined) } }))

const LINK: JoinLink = { url: 'http://192.168.1.42:5173/join/play-1', reachableFromLan: true }

function person(id: string, overrides: Partial<ParticipantDto> = {}): ParticipantDto {
  return {
    id,
    sessionId: 'play-1',
    displayName: id,
    role: ParticipantRole.Player,
    characterId: null,
    isApproved: true,
    joinedAt: '2026-10-01T10:00:00Z',
    lastSeenAt: '2026-10-01T10:00:00Z',
    ...overrides,
  }
}

type Props = {
  inviteCode?: string | null
  joinLink?: JoinLink
  pending?: ParticipantDto[]
  latePlayers?: ParticipantDto[]
  busy?: boolean
}

function mountPanel(props: Props = {}) {
  return mount(InvitePlayersPanel, {
    props: { inviteCode: 'ABCD1234', joinLink: LINK, pending: [], latePlayers: [], ...props },
  })
}

/** Pretends the browser window is narrow (a phone) or wide (a desktop). */
function stubScreen(narrow: boolean): void {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({ matches: narrow && query.includes('max-width'), media: query })),
  )
}

const body = (w: ReturnType<typeof mountPanel>) => w.find('[data-testid="invite-body"]')
const toggle = (w: ReturnType<typeof mountPanel>) => w.find('[data-testid="invite-toggle"]')
const isOpen = (w: ReturnType<typeof mountPanel>) => body(w).attributes('style')?.includes('display: none') !== true

beforeEach(() => {
  vi.unstubAllGlobals()
})
afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('InvitePlayersPanel: collapsed and expanded', () => {
  it('is open on a wide screen', () => {
    stubScreen(false)

    const wrapper = mountPanel()

    expect(isOpen(wrapper)).toBe(true)
    expect(toggle(wrapper).attributes('aria-expanded')).toBe('true')
  })

  it('is collapsed by default on a narrow screen', () => {
    stubScreen(true)

    const wrapper = mountPanel()

    expect(isOpen(wrapper)).toBe(false)
    expect(toggle(wrapper).attributes('aria-expanded')).toBe('false')
    // Still in the page (collapsed, not removed), and the header stays visible.
    expect(wrapper.find('h3').text()).toContain('Invite players')
  })

  it('opens and closes with the header button', async () => {
    stubScreen(true)
    const wrapper = mountPanel()

    await toggle(wrapper).trigger('click')
    expect(isOpen(wrapper)).toBe(true)
    expect(toggle(wrapper).attributes('aria-expanded')).toBe('true')

    await toggle(wrapper).trigger('click')
    expect(isOpen(wrapper)).toBe(false)
  })

  it('links the header button to the panel body for assistive technology', () => {
    const wrapper = mountPanel()

    expect(toggle(wrapper).attributes('aria-controls')).toBe(body(wrapper).attributes('id'))
  })

  it('treats a browser that cannot report its size as a wide screen', () => {
    vi.stubGlobal('matchMedia', undefined)

    expect(isOpen(mountPanel())).toBe(true)
  })
})

describe('InvitePlayersPanel: join details', () => {
  it('shows the join link, the QR code and the invite code', () => {
    const wrapper = mountPanel()

    expect(wrapper.find('[data-testid="invite-url"]').text()).toBe('http://192.168.1.42:5173/join/play-1')
    expect(wrapper.find('[data-testid="invite-code"]').text()).toBe('ABCD1234')
    expect(wrapper.find('canvas').exists()).toBe(true)
  })

  it('copies the join link and the invite code', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const wrapper = mountPanel()

    await wrapper.find('[data-testid="copy-url"]').trigger('click')
    await wrapper.find('[data-testid="copy-code"]').trigger('click')

    expect(writeText).toHaveBeenNthCalledWith(1, 'http://192.168.1.42:5173/join/play-1')
    expect(writeText).toHaveBeenNthCalledWith(2, 'ABCD1234')
  })

  it('confirms a copy', async () => {
    vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
    const wrapper = mountPanel()

    await wrapper.find('[data-testid="copy-code"]').trigger('click')
    await Promise.resolve()

    expect(wrapper.find('[data-testid="copy-code"]').text()).toContain('Copied')
  })

  it('does not break when the clipboard is unavailable', async () => {
    vi.stubGlobal('navigator', {})
    const wrapper = mountPanel()

    await wrapper.find('[data-testid="copy-url"]').trigger('click')

    expect(wrapper.find('[data-testid="copy-url"]').text()).toBe('Copy')
  })

  it('warns when the link only works on the host computer', () => {
    const wrapper = mountPanel({ joinLink: { url: 'http://localhost:5173/join/play-1', reachableFromLan: false } })

    expect(wrapper.find('[data-testid="invite-no-lan"]').exists()).toBe(true)
  })

  it('has no such warning for a LAN link', () => {
    expect(mountPanel().find('[data-testid="invite-no-lan"]').exists()).toBe(false)
  })
})

describe('InvitePlayersPanel: the invite code is missing', () => {
  it('says it cannot be shown again, and why', () => {
    const wrapper = mountPanel({ inviteCode: null })

    const message = wrapper.find('[data-testid="invite-code-missing"]').text()
    expect(message).toMatch(/can't be shown again/i)
    expect(message).toMatch(/only a hash/i)
    expect(wrapper.find('[data-testid="invite-code"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="copy-code"]').exists()).toBe(false)
  })

  it('still shows the join link', () => {
    const wrapper = mountPanel({ inviteCode: null })

    expect(wrapper.find('[data-testid="invite-url"]').text()).toContain('/join/play-1')
  })

  it('offers "Create a new play session" but disabled, and explains why', () => {
    const wrapper = mountPanel({ inviteCode: null })

    const button = wrapper.find('[data-testid="new-session-button"]')
    expect(button.text()).toBe('Create a new play session')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('aria-describedby')).toBe('new-session-reason')
    const reason = wrapper.find('[data-testid="new-session-reason"]').text()
    expect(reason).toMatch(/players who join a new session can't be added/i)
    expect(reason).toMatch(/abandon this adventure/i)
  })

  it('does not show the missing-code message when the code is available', () => {
    expect(mountPanel().find('[data-testid="invite-code-missing"]').exists()).toBe(false)
  })
})

describe('InvitePlayersPanel: attention badge', () => {
  it('is hidden when nothing needs the host', () => {
    expect(mountPanel().find('[data-testid="invite-badge"]').exists()).toBe(false)
  })

  it('counts requests waiting for approval', () => {
    const wrapper = mountPanel({ pending: [person('a', { isApproved: false }), person('b', { isApproved: false })] })

    expect(wrapper.find('[data-testid="invite-badge"]').text()).toContain('2')
  })

  it('counts approved players waiting to be added', () => {
    const wrapper = mountPanel({ latePlayers: [person('dan')] })

    expect(wrapper.find('[data-testid="invite-badge"]').text()).toContain('1')
  })

  it('adds the two together', () => {
    const wrapper = mountPanel({
      pending: [person('a', { isApproved: false })],
      latePlayers: [person('dan'), person('eve')],
    })

    expect(wrapper.find('[data-testid="invite-badge"]').text()).toContain('3')
  })

  it('is visible while the panel is collapsed', () => {
    stubScreen(true)
    const wrapper = mountPanel({ pending: [person('a', { isApproved: false })] })

    expect(isOpen(wrapper)).toBe(false)
    expect(toggle(wrapper).find('[data-testid="invite-badge"]').exists()).toBe(true)
  })

  it('is described for screen readers', () => {
    const wrapper = mountPanel({
      pending: [person('a', { isApproved: false })],
      latePlayers: [person('dan')],
    })

    const label = wrapper.find('[data-testid="invite-badge"]').text()
    expect(label).toContain('1 waiting for approval')
    expect(label).toContain('1 to add to the adventure')
  })

  it('goes away when the list empties', async () => {
    const wrapper = mountPanel({ latePlayers: [person('dan')] })
    expect(wrapper.find('[data-testid="invite-badge"]').exists()).toBe(true)

    await wrapper.setProps({ latePlayers: [] })

    expect(wrapper.find('[data-testid="invite-badge"]').exists()).toBe(false)
  })
})

describe('InvitePlayersPanel: approving', () => {
  it('lists people waiting and emits approve', async () => {
    const wrapper = mountPanel({ pending: [person('newcomer', { isApproved: false })] })

    expect(wrapper.find('[data-testid="invite-pending"]').text()).toContain('newcomer')
    await wrapper.find('[aria-label="Approve newcomer"]').trigger('click')

    expect(wrapper.emitted('approve')).toEqual([['newcomer']])
  })

  it('shows nothing when nobody is waiting', () => {
    expect(mountPanel().find('[data-testid="invite-pending"]').exists()).toBe(false)
  })

  it('disables Approve while an action is in flight', () => {
    const wrapper = mountPanel({ pending: [person('newcomer', { isApproved: false })], busy: true })

    expect(wrapper.find('[data-testid="invite-approve"]').attributes('disabled')).toBeDefined()
  })
})

describe('InvitePlayersPanel: "Add to the end"', () => {
  it('is shown for an approved player who is not in the turn order, and emits add', async () => {
    const wrapper = mountPanel({ latePlayers: [person('dan')] })

    expect(wrapper.find('[data-testid="late-players"]').text()).toContain('dan')
    await wrapper.find('[data-testid="add-late-player"]').trigger('click')

    expect(wrapper.emitted('add')).toEqual([['dan']])
  })

  it('is not shown when nobody is waiting to be added', () => {
    const wrapper = mountPanel()

    expect(wrapper.find('[data-testid="late-players"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="add-late-player"]').exists()).toBe(false)
  })

  it('is not offered for someone who is only waiting for approval', () => {
    const wrapper = mountPanel({ pending: [person('newcomer', { isApproved: false })] })

    expect(wrapper.find('[data-testid="add-late-player"]').exists()).toBe(false)
  })

  it('has one button per player, each with its own name', () => {
    const wrapper = mountPanel({ latePlayers: [person('dan'), person('eve')] })

    expect(wrapper.findAll('[data-testid="add-late-player"]')).toHaveLength(2)
    expect(wrapper.find('[aria-label="Add eve to the end of the turn order"]').exists()).toBe(true)
  })

  it('is disabled while an action is in flight', () => {
    const wrapper = mountPanel({ latePlayers: [person('dan')], busy: true })

    expect(wrapper.find('[data-testid="add-late-player"]').attributes('disabled')).toBeDefined()
  })

  it('says whose turn and which round it leaves alone', () => {
    const wrapper = mountPanel({ latePlayers: [person('dan')] })

    expect(wrapper.find('[data-testid="late-players"]').text()).toMatch(/don't change/i)
  })

  it('is reachable while the panel is collapsed only after opening it, and the badge says it is waiting', async () => {
    stubScreen(true)
    const wrapper = mountPanel({ latePlayers: [person('dan')] })

    expect(isOpen(wrapper)).toBe(false)
    expect(wrapper.find('[data-testid="invite-badge"]').exists()).toBe(true)
    await toggle(wrapper).trigger('click')
    expect(isOpen(wrapper)).toBe(true)
    expect(wrapper.find('[data-testid="add-late-player"]').isVisible()).toBe(true)
  })
})

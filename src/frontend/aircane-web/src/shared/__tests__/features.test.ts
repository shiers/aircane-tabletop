import { describe, it, expect, afterEach, vi } from 'vitest'
import { computed } from 'vue'
import { features, parseFlag } from '../features'

describe('parseFlag', () => {
  it.each(['true', 'TRUE', 'True', ' true ', '1', 'yes', 'on', 'ON'])('reads %j as on', (value) => {
    expect(parseFlag(value)).toBe(true)
  })

  it.each([undefined, null, '', ' ', 'false', '0', 'no', 'off', 'maybe', 'truthy', 1, true])(
    'reads %j as off (only the strings true, 1, yes and on turn a flag on)',
    (value) => {
      expect(parseFlag(value)).toBe(false)
    },
  )
})

describe('features', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    features.characters = true
  })

  /** A fresh copy of the module, which reads the (stubbed) environment again. */
  async function freshFeatures() {
    vi.resetModules()
    return (await import('../features')).features
  }

  it('turns Characters on from VITE_ENABLE_CHARACTERS=true', async () => {
    vi.stubEnv('VITE_ENABLE_CHARACTERS', 'true')

    expect((await freshFeatures()).characters).toBe(true)
  })

  it('leaves Characters off when the variable is empty, which is how an unset variable reads', async () => {
    vi.stubEnv('VITE_ENABLE_CHARACTERS', '')

    expect((await freshFeatures()).characters).toBe(false)
  })

  it('leaves Characters off for VITE_ENABLE_CHARACTERS=false', async () => {
    vi.stubEnv('VITE_ENABLE_CHARACTERS', 'false')

    expect((await freshFeatures()).characters).toBe(false)
  })

  it('is reactive, so what reads a flag updates when it changes', () => {
    const label = computed(() => (features.characters ? 'shown' : 'hidden'))
    features.characters = true
    expect(label.value).toBe('shown')

    features.characters = false
    expect(label.value).toBe('hidden')
  })
})

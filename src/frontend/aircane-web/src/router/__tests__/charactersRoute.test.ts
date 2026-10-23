import { describe, it, expect, afterEach } from 'vitest'
import router from '../index'
import { features } from '@/shared/features'

/**
 * The Characters routes stay defined whatever the flag says; while it is off they lead to the dashboard, so a
 * bookmark or a typed address never shows a broken page. Each test starts from /about, so ending on the
 * dashboard shows a redirect rather than a navigation that never happened.
 */
describe('Characters routes and the Characters feature flag', () => {
  afterEach(() => {
    features.characters = true
  })

  it('keeps both route definitions whatever the flag says', () => {
    features.characters = false

    expect(router.hasRoute('characters')).toBe(true)
    expect(router.hasRoute('character-field-review')).toBe(true)
  })

  it.each([
    '/characters',
    '/characters/char-1/field-review',
    '/characters/char-1/field-review?warnings=%5B%5D&unmappedFields=%5B%5D',
  ])('with the feature off, %s redirects to the dashboard', async (target) => {
    features.characters = false
    await router.push('/about')
    expect(router.currentRoute.value.name).toBe('about')

    await router.push(target)

    expect(router.currentRoute.value.name).toBe('home')
    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('with the feature on, /characters opens the Characters page', async () => {
    features.characters = true
    await router.push('/about')

    await router.push('/characters')

    expect(router.currentRoute.value.name).toBe('characters')
  })

  it('with the feature on, the field review route opens', async () => {
    features.characters = true
    await router.push('/about')

    await router.push('/characters/char-1/field-review')

    expect(router.currentRoute.value.name).toBe('character-field-review')
    expect(router.currentRoute.value.params.characterId).toBe('char-1')
  })
})

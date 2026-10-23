import { describe, expect, it } from 'vitest'
import { SourceType } from '../../api'
import {
  FALLBACK_THUMBNAIL_KEY,
  THUMBNAIL_MANIFEST,
  resolveThumbnailKey,
  thumbnailUrl,
} from '..'

const doc = (sourceType: SourceType, gameSystem: string | null = null, ruleset: string | null = null) => ({
  sourceType,
  gameSystem,
  ruleset,
})

const ALIASES: Array<[string, string]> = [
  ['Pathfinder', 'fantasy-knight-pf'],
  ['pf2e', 'fantasy-knight-pf'],
  ['Call of Cthulhu', 'cosmic-horror-cc'],
  ['Cthulhu', 'cosmic-horror-cc'],
  ['CoC', 'cosmic-horror-cc'],
  ['Alien', 'scifi-horror-al'],
  ['Daggerheart', 'crystal-fantasy-dg'],
  ['GURPS', 'multigenre-gp'],
  ['Shadowrun', 'cyberpunk-fantasy-sr'],
  ['Vampire', 'gothic-vampire-vm'],
  ['Masquerade', 'gothic-vampire-vm'],
  ['VtM', 'gothic-vampire-vm'],
  ['Star Wars', 'space-opera-sw'],
  ['Star Trek', 'exploration-scifi-st'],
  ['Black Crusade', 'grimdark-soldier-bc'],
  ['Rogue Trader', 'grimdark-captain-rt'],
  ['Dark Heresy', 'grimdark-inquisitor-dh'],
  ['Deathwatch', 'grimdark-soldier-dw'],
  ['Warhammer', 'grimdark-soldier-dw'],
  ['40k', 'grimdark-soldier-dw'],
]

describe('resolveThumbnailKey', () => {
  it.each(ALIASES)('maps gameSystem "%s" to %s', (system, key) => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, system))).toBe(key)
  })

  it('also reads the ruleset', () => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, null, 'Pathfinder 2e'))).toBe('fantasy-knight-pf')
  })

  it('is case-insensitive', () => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'sHaDoWrUn'))).toBe('cyberpunk-fantasy-sr')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'STAR WARS'))).toBe('space-opera-sw')
  })

  it('treats hyphens and extra spaces inside a multi-word alias as the same alias', () => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'star-wars'))).toBe('space-opera-sw')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Dark   Heresy'))).toBe('grimdark-inquisitor-dh')
  })

  it('matches whole words only', () => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'alienist'))).toBe('fantasy-rulebook-dd')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Aliens'))).toBe('fantasy-rulebook-dd')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'coconut'))).toBe('fantasy-rulebook-dd')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Alien RPG'))).toBe('scifi-horror-al')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Alien: Evolved'))).toBe('scifi-horror-al')
  })

  it('prefers the specific Warhammer line over the generic Warhammer match', () => {
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Warhammer 40k', 'Dark Heresy'))).toBe('grimdark-inquisitor-dh')
    expect(resolveThumbnailKey(doc(SourceType.Rules, 'Warhammer 40,000', 'Rogue Trader'))).toBe('grimdark-captain-rt')
  })

  it('homebrew source type always wins, even over a recognised system', () => {
    expect(resolveThumbnailKey(doc(SourceType.Homebrew, 'Pathfinder'))).toBe('homebrew')
    expect(resolveThumbnailKey(doc(SourceType.Homebrew))).toBe('homebrew')
  })

  it.each(['D&D', 'dnd', 'Dungeons & Dragons', '5e', 'SRD', 'D&D 5e'])(
    'splits "%s" into adventure art for Adventure/Solo and rulebook art otherwise',
    (system) => {
      expect(resolveThumbnailKey(doc(SourceType.Adventure, system))).toBe('cavern-adventure-lm')
      expect(resolveThumbnailKey(doc(SourceType.Solo, system))).toBe('cavern-adventure-lm')
      expect(resolveThumbnailKey(doc(SourceType.Rules, system))).toBe('fantasy-rulebook-dd')
      expect(resolveThumbnailKey(doc(SourceType.Character, system))).toBe('fantasy-rulebook-dd')
    },
  )

  it('lets a named system beat the D&D rule, so a Pathfinder adventure shows the knight', () => {
    expect(resolveThumbnailKey(doc(SourceType.Adventure, 'Pathfinder', '5e compatible'))).toBe('fantasy-knight-pf')
  })

  it('falls back by source type when nothing matches', () => {
    expect(resolveThumbnailKey(doc(SourceType.Adventure, 'Unheard Of'))).toBe('cavern-adventure-lm')
    expect(resolveThumbnailKey(doc(SourceType.Solo))).toBe('cavern-adventure-lm')
    expect(resolveThumbnailKey(doc(SourceType.Rules))).toBe('fantasy-rulebook-dd')
    for (const t of [SourceType.Unknown, SourceType.Character, SourceType.Generated]) {
      expect(resolveThumbnailKey(doc(t))).toBe('generic')
    }
  })

  it('returns a stored key unchanged and ignores the automatic rules', () => {
    expect(resolveThumbnailKey(doc(SourceType.Homebrew), 'space-opera-sw')).toBe('space-opera-sw')
    expect(resolveThumbnailKey(doc(SourceType.Rules), undefined)).toBe('fantasy-rulebook-dd')
    expect(resolveThumbnailKey(doc(SourceType.Rules), null)).toBe('fantasy-rulebook-dd')
  })

  it('only ever returns keys that exist in the manifest', () => {
    const keys = new Set(THUMBNAIL_MANIFEST.map((e) => e.key))
    const results = [
      ...ALIASES.map(([s]) => resolveThumbnailKey(doc(SourceType.Rules, s))),
      resolveThumbnailKey(doc(SourceType.Homebrew)),
      resolveThumbnailKey(doc(SourceType.Adventure)),
      resolveThumbnailKey(doc(SourceType.Rules)),
      resolveThumbnailKey(doc(SourceType.Unknown)),
    ]
    for (const k of results) expect(keys.has(k)).toBe(true)
  })
})

describe('thumbnailUrl', () => {
  it.each(THUMBNAIL_MANIFEST.map((e) => [e.key, e.file]))('builds the URL for %s', (key, file) => {
    expect(thumbnailUrl(key)).toBe(`/assets/document-thumbnails/${file}`)
  })

  it('falls back to the generic thumbnail for a key that is not in the manifest', () => {
    expect(thumbnailUrl('does-not-exist')).toBe(thumbnailUrl(FALLBACK_THUMBNAIL_KEY))
    expect(thumbnailUrl('')).toBe('/assets/document-thumbnails/generic.webp')
  })
})

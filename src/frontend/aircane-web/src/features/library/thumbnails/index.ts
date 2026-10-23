import { SourceType, type SourceDocumentDto } from '../api'
import { THUMBNAIL_MANIFEST, type ThumbnailEntry } from './manifest.generated'

export { THUMBNAIL_MANIFEST, type ThumbnailEntry }

export const THUMBNAIL_BASE_PATH = '/assets/document-thumbnails'
export const FALLBACK_THUMBNAIL_KEY = 'generic'

const ADVENTURE_KEY = 'cavern-adventure-lm'
const RULEBOOK_KEY = 'fantasy-rulebook-dd'

type ThumbnailDoc = Pick<SourceDocumentDto, 'sourceType' | 'gameSystem' | 'ruleset'>

/**
 * Whole-word matcher. Spaces inside a term match any run of whitespace, hyphens or underscores,
 * and a term must not touch another letter or digit ("alienist" does not match "alien").
 */
function wordMatcher(terms: string[]): RegExp {
  const alternatives = terms.map((t) =>
    t
      .toLowerCase()
      .replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
      .replace(/ /g, '[\\s_-]+'),
  )
  return new RegExp(`(?<![a-z0-9])(?:${alternatives.join('|')})(?![a-z0-9])`, 'i')
}

/** Order matters: the first rule that matches wins, so specific titles sit before "warhammer". */
const SYSTEM_RULES: ReadonlyArray<{ match: RegExp; key: string }> = [
  { terms: ['pathfinder', 'pf2e'], key: 'fantasy-knight-pf' },
  { terms: ['call of cthulhu', 'cthulhu', 'coc'], key: 'cosmic-horror-cc' },
  { terms: ['alien'], key: 'scifi-horror-al' },
  { terms: ['daggerheart'], key: 'crystal-fantasy-dg' },
  { terms: ['gurps'], key: 'multigenre-gp' },
  { terms: ['shadowrun'], key: 'cyberpunk-fantasy-sr' },
  { terms: ['vampire', 'masquerade', 'vtm'], key: 'gothic-vampire-vm' },
  { terms: ['star wars'], key: 'space-opera-sw' },
  { terms: ['star trek'], key: 'exploration-scifi-st' },
  { terms: ['black crusade'], key: 'grimdark-soldier-bc' },
  { terms: ['rogue trader'], key: 'grimdark-captain-rt' },
  { terms: ['dark heresy'], key: 'grimdark-inquisitor-dh' },
  { terms: ['deathwatch', 'warhammer', '40k'], key: 'grimdark-soldier-dw' },
].map(({ terms, key }) => ({ match: wordMatcher(terms), key }))

const DND_MATCH = wordMatcher(['d&d', 'dnd', 'dungeons', '5e', 'srd'])

function isAdventureLike(sourceType: SourceType): boolean {
  return sourceType === SourceType.Adventure || sourceType === SourceType.Solo
}

/**
 * Pick the thumbnail key for a document. A stored (user-chosen) key always wins; none exists yet,
 * so callers currently pass nothing. Automatic choice only looks at gameSystem and ruleset, never the title.
 */
export function resolveThumbnailKey(doc: ThumbnailDoc, storedKey?: string | null): string {
  if (storedKey) return storedKey
  if (doc.sourceType === SourceType.Homebrew) return 'homebrew'

  const haystack = `${doc.gameSystem ?? ''} ${doc.ruleset ?? ''}`
  const rule = SYSTEM_RULES.find((r) => r.match.test(haystack))
  if (rule) return rule.key

  if (DND_MATCH.test(haystack)) return isAdventureLike(doc.sourceType) ? ADVENTURE_KEY : RULEBOOK_KEY

  if (isAdventureLike(doc.sourceType)) return ADVENTURE_KEY
  if (doc.sourceType === SourceType.Rules) return RULEBOOK_KEY
  return FALLBACK_THUMBNAIL_KEY
}

const FILE_BY_KEY: ReadonlyMap<string, string> = new Map(THUMBNAIL_MANIFEST.map((e) => [e.key, e.file]))

/** URL for a key. A key that is not in the manifest resolves to the generic thumbnail. */
export function thumbnailUrl(key: string): string {
  const file = FILE_BY_KEY.get(key) ?? FILE_BY_KEY.get(FALLBACK_THUMBNAIL_KEY) ?? `${FALLBACK_THUMBNAIL_KEY}.webp`
  return `${THUMBNAIL_BASE_PATH}/${file}`
}

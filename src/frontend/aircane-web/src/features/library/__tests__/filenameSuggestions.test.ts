import { describe, it, expect } from 'vitest'
import { suggestTitleFromFilename, detectRulesetYear } from '../filenameSuggestions'

describe('suggestTitleFromFilename', () => {
  it('strips the file extension', () => {
    expect(suggestTitleFromFilename('Player Handbook.pdf')).toBe('Player Handbook')
  })

  it('strips parenthetical OCR/scan qualifiers', () => {
    expect(suggestTitleFromFilename('Dungeon Masters Guide (Color OCR).pdf')).toBe(
      'Dungeon Masters Guide',
    )
    expect(suggestTitleFromFilename('Players Handbook (BnW OCR).pdf')).toBe('Players Handbook')
  })

  it('strips bracketed qualifiers', () => {
    expect(suggestTitleFromFilename("Mordenkainen's Tome of Foes [Deluxe].pdf")).toBe(
      "Mordenkainen's Tome of Foes",
    )
  })

  it('expands DnD to D&D 5e when an edition marker is present', () => {
    expect(suggestTitleFromFilename('DnD 5e Dungeon Masters Guide (Color OCR).pdf')).toBe(
      'D&D 5e Dungeon Masters Guide',
    )
  })

  it('normalizes D&D 5e spelling variants', () => {
    expect(suggestTitleFromFilename('D&D5e Players Handbook.pdf')).toBe('D&D 5e Players Handbook')
  })

  it('expands standalone DnD to D&D without inventing an edition', () => {
    expect(suggestTitleFromFilename('DnD Adventures.pdf')).toBe('D&D Adventures')
  })

  it('expands Pf2e to Pathfinder 2e', () => {
    expect(suggestTitleFromFilename('Pf2e Core Rules.pdf')).toBe('Pathfinder 2e Core Rules')
  })

  it('replaces underscores and collapses whitespace', () => {
    expect(suggestTitleFromFilename('Volos_Guide__to_Monsters.pdf')).toBe('Volos Guide to Monsters')
  })

  it('removes stray leading/trailing dashes left by qualifier removal', () => {
    expect(suggestTitleFromFilename('Volos Guide to Monsters - Maps.pdf')).toBe(
      'Volos Guide to Monsters - Maps',
    )
    // A qualifier that becomes a trailing dash is cleaned up.
    expect(suggestTitleFromFilename('Some Book - (Color OCR).pdf')).toBe('Some Book')
  })

  it('preserves dots that are not the extension', () => {
    expect(suggestTitleFromFilename('Rules v1.2 Errata.pdf')).toBe('Rules v1.2 Errata')
  })

  it('returns empty string for empty or whitespace input', () => {
    expect(suggestTitleFromFilename('')).toBe('')
    expect(suggestTitleFromFilename('   ')).toBe('')
  })
})

describe('detectRulesetYear', () => {
  it('detects a standalone 4-digit year', () => {
    expect(detectRulesetYear('D&D 5e Players Handbook 2014.pdf')).toBe('2014')
    expect(detectRulesetYear('Players Handbook (2024).pdf')).toBe('2024')
  })

  it('returns null when no year is present', () => {
    expect(detectRulesetYear('Dungeon Masters Guide (Color OCR).pdf')).toBeNull()
  })

  it('does not match years glued to other digits', () => {
    expect(detectRulesetYear('Book12014edition.pdf')).toBeNull()
  })

  it('ignores implausible year-like numbers outside the 1900-2099 range', () => {
    expect(detectRulesetYear('Roll 1800 gold.pdf')).toBeNull() // 18xx not in 19xx/20xx range
    expect(detectRulesetYear('Page 3021.pdf')).toBeNull() // 30xx out of range
  })

  it('returns null for empty input', () => {
    expect(detectRulesetYear('')).toBeNull()
  })
})

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { previewCharacterForm } from '../api'
import { fieldTypeFromWire, type FieldType } from '../types'
import apiClient from '@/shared/api/client'

// Mock the shared axios instance so no real HTTP calls are made.
vi.mock('@/shared/api/client', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

const mockedPost = vi.mocked(apiClient.post)

describe('fieldTypeFromWire', () => {
  it('maps every integer wire value 0-9 to the correct FieldType string', () => {
    const expected: FieldType[] = [
      'text', // 0
      'number', // 1
      'boolean', // 2
      'enum', // 3
      'dice_expression', // 4
      'list', // 5
      'repeating', // 6
      'resource_pool', // 7
      'calculated', // 8
      'grouped', // 9
    ]
    expected.forEach((kind, i) => {
      expect(fieldTypeFromWire(i)).toBe(kind)
    })
  })

  it('passes a valid FieldType string through unchanged', () => {
    const strings: FieldType[] = [
      'text',
      'number',
      'boolean',
      'enum',
      'dice_expression',
      'list',
      'repeating',
      'resource_pool',
      'calculated',
      'grouped',
    ]
    for (const s of strings) {
      expect(fieldTypeFromWire(s)).toBe(s)
    }
  })

  it('falls back to text for an out-of-range integer or garbage value', () => {
    expect(fieldTypeFromWire(10)).toBe('text')
    expect(fieldTypeFromWire(-1)).toBe('text')
    expect(fieldTypeFromWire(1.5)).toBe('text')
    expect(fieldTypeFromWire('bogus')).toBe('text')
    expect(fieldTypeFromWire(null)).toBe('text')
    expect(fieldTypeFromWire(undefined)).toBe('text')
  })
})

describe('previewCharacterForm', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('normalizes integer wire field types to FieldType string discriminants', async () => {
    // The backend serializes field types as integer enum values (no JsonStringEnumConverter).
    mockedPost.mockResolvedValueOnce({
      data: {
        sections: [
          {
            id: 'basics',
            label: 'Basics',
            fields: [
              { id: 'name', type: 0, label: 'Name', required: true },
              { id: 'level', type: 1, label: 'Level', required: true },
              { id: 'class', type: 3, label: 'Class', required: false, options: ['Fighter', 'Wizard'] },
            ],
          },
          {
            id: 'combat',
            label: 'Combat',
            fields: [
              { id: 'hp', type: 7, label: 'HP', required: false },
              { id: 'ac', type: 8, label: 'AC', required: false },
            ],
          },
        ],
      },
    })

    const descriptor = await previewCharacterForm('gs-1')

    expect(mockedPost).toHaveBeenCalledWith('/api/game-systems/gs-1/preview-character-form', {})
    const [basics, combat] = descriptor.sections
    expect(basics.fields.map((f) => f.type)).toEqual(['text', 'number', 'enum'])
    expect(combat.fields.map((f) => f.type)).toEqual(['resource_pool', 'calculated'])
    // Non-type props are preserved.
    expect(basics.fields[2].options).toEqual(['Fighter', 'Wizard'])
  })
})

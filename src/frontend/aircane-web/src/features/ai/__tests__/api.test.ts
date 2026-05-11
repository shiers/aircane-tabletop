import { describe, it, expect } from 'vitest'
import { AiProviderType } from '../api'

describe('AiProviderType enum', () => {
  it('has expected values', () => {
    expect(AiProviderType.Fake).toBe(0)
    expect(AiProviderType.OpenAi).toBe(1)
    expect(AiProviderType.AzureOpenAi).toBe(2)
    expect(AiProviderType.AwsBedrock).toBe(3)
    expect(AiProviderType.Ollama).toBe(4)
    expect(AiProviderType.Grok).toBe(5)
  })
})

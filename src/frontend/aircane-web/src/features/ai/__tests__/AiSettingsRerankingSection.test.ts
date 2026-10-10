import { describe, it, expect, vi, beforeEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import vBgAsset from '@/directives/vBgAsset'

const api = vi.hoisted(() => ({
  getAiConfig: vi.fn(),
  updateAiConfig: vi.fn(),
  testAiConnection: vi.fn(),
  testRerankingConnection: vi.fn(),
  getProviders: vi.fn(),
  getAvailableModels: vi.fn(),
}))

vi.mock('../api', async () => {
  const actual = await vi.importActual<typeof import('../api')>('../api')
  return { ...actual, ...api }
})
vi.mock('@/shared/tauri/bridge', () => ({
  isDesktop: () => false,
  invoke: vi.fn(),
  openExternal: vi.fn(),
}))

import AiSettingsView from '../AiSettingsView.vue'

const baseConfig = {
  activeProvider: 0,
  reranking: {
    provider: 'Cohere',
    ollama: { model: 'bge-reranker-base' },
    cohere: { apiKey: '**********1234', model: 'rerank-english-v3.0' },
  },
}

async function mountView(config = baseConfig) {
  api.getAiConfig.mockResolvedValue(config)
  api.getProviders.mockResolvedValue([{ id: 0, name: 'Fake (Development)', description: 'x' }])
  api.getAvailableModels.mockResolvedValue([])
  api.updateAiConfig.mockResolvedValue(config)

  const wrapper = mount(AiSettingsView, {
    global: {
      directives: { 'bg-asset': vBgAsset },
      stubs: { LicenseAttributionModal: true },
    },
  })
  await flushPromises()
  return wrapper
}

describe('AiSettingsView reranking section', () => {
  beforeEach(() => {
    Object.values(api).forEach((fn) => fn.mockReset())
  })

  it('shows the saved provider and model', async () => {
    const wrapper = await mountView()

    const select = wrapper.get('#reranking-provider').element as HTMLSelectElement
    expect(select.value).toBe('Cohere')
    expect((wrapper.get('#rerank-cohere-model').element as HTMLInputElement).value).toBe('rerank-english-v3.0')
    // The key arrives masked and is shown masked.
    expect((wrapper.get('#rerank-cohere-key').element as HTMLInputElement).value).toBe('**********1234')
  })

  it('offers None, Ollama and Cohere, and hides provider fields for None', async () => {
    const wrapper = await mountView({ ...baseConfig, reranking: { ...baseConfig.reranking, provider: 'None' } })

    const options = wrapper.get('#reranking-provider').findAll('option').map((o) => o.text())
    expect(options).toEqual(['None', 'Ollama', 'Cohere'])
    expect(wrapper.find('#rerank-cohere-key').exists()).toBe(false)
    expect(wrapper.find('#rerank-ollama-model').exists()).toBe(false)
    expect(wrapper.find('[data-testid="test-reranking"]').exists()).toBe(false)
  })

  it('shows the Ollama model field when Ollama is selected', async () => {
    const wrapper = await mountView({ ...baseConfig, reranking: { ...baseConfig.reranking, provider: 'None' } })

    await wrapper.get('#reranking-provider').setValue('Ollama')

    expect((wrapper.get('#rerank-ollama-model').element as HTMLInputElement).value).toBe('bge-reranker-base')
    expect(wrapper.find('#rerank-cohere-key').exists()).toBe(false)
  })

  it('does not resend a masked key when saving', async () => {
    const wrapper = await mountView()

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const request = api.updateAiConfig.mock.calls[0][0]
    expect(request.reranking.provider).toBe('Cohere')
    expect(request.reranking.cohere.apiKey).toBeUndefined()
    expect(request.reranking.cohere.model).toBe('rerank-english-v3.0')
  })

  it('sends a newly typed key when saving', async () => {
    const wrapper = await mountView()

    await wrapper.get('#rerank-cohere-key').setValue('new-cohere-key')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(api.updateAiConfig.mock.calls[0][0].reranking.cohere.apiKey).toBe('new-cohere-key')
  })

  it('tests the connection with the form values and shows the result', async () => {
    api.testRerankingConnection.mockResolvedValue({ available: false, message: 'Cohere API key is invalid.' })
    const wrapper = await mountView()

    await wrapper.get('[data-testid="test-reranking"]').trigger('click')
    await flushPromises()

    expect(api.testRerankingConnection).toHaveBeenCalledWith({
      provider: 'Cohere',
      ollama: { model: 'bge-reranker-base' },
      cohere: { apiKey: undefined, model: 'rerank-english-v3.0' },
    })
    expect(wrapper.get('[data-testid="reranking-test-result"]').text()).toBe('✗ Cohere API key is invalid.')
  })

  it('shows a success result', async () => {
    api.testRerankingConnection.mockResolvedValue({ available: true, message: 'Connected to Cohere.' })
    const wrapper = await mountView()

    await wrapper.get('[data-testid="test-reranking"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="reranking-test-result"]').text()).toBe('✓ Connected to Cohere.')
  })

  it('clears a stale test result when the provider changes', async () => {
    api.testRerankingConnection.mockResolvedValue({ available: true, message: 'ok' })
    const wrapper = await mountView()
    await wrapper.get('[data-testid="test-reranking"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="reranking-test-result"]').exists()).toBe(true)

    await wrapper.get('#reranking-provider').setValue('Ollama')

    expect(wrapper.find('[data-testid="reranking-test-result"]').exists()).toBe(false)
  })
})

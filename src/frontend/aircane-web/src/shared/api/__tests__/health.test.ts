import { describe, it, expect, vi, beforeEach } from 'vitest'
import { checkHealth } from '../health'
import apiClient from '../client'

// Mock the shared axios instance so no real HTTP calls are made
vi.mock('../client', () => ({
  default: {
    get: vi.fn(),
  },
}))

const mockedGet = vi.mocked(apiClient.get)

describe('checkHealth', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns the health response when the backend is healthy', async () => {
    mockedGet.mockResolvedValueOnce({ data: { status: 'healthy' } })

    const result = await checkHealth()

    expect(mockedGet).toHaveBeenCalledWith('/health', { timeout: 5_000 })
    expect(result).toEqual({ status: 'healthy' })
  })

  it('propagates errors when the request fails', async () => {
    mockedGet.mockRejectedValueOnce(new Error('Network Error'))

    await expect(checkHealth()).rejects.toThrow('Network Error')
    expect(mockedGet).toHaveBeenCalledWith('/health', { timeout: 5_000 })
  })

  it('returns whatever status string the backend sends', async () => {
    mockedGet.mockResolvedValueOnce({ data: { status: 'degraded' } })

    const result = await checkHealth()

    expect(result.status).toBe('degraded')
  })
})

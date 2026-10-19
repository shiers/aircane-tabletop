import { describe, it, expect, vi, afterEach } from 'vitest'
import { onPageVisible } from '../visibility'

function setVisibility(state: DocumentVisibilityState): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
}

function pageShow(persisted: boolean): void {
  const event = new Event('pageshow') as PageTransitionEvent
  Object.defineProperty(event, 'persisted', { value: persisted })
  window.dispatchEvent(event)
}

describe('onPageVisible', () => {
  afterEach(() => {
    setVisibility('visible')
  })

  it('calls back when the page becomes visible again', () => {
    const callback = vi.fn()
    const stop = onPageVisible(callback)

    setVisibility('hidden')
    document.dispatchEvent(new Event('visibilitychange'))
    expect(callback).not.toHaveBeenCalled()

    setVisibility('visible')
    document.dispatchEvent(new Event('visibilitychange'))
    expect(callback).toHaveBeenCalledTimes(1)
    stop()
  })

  it('calls back when the page is restored from the back/forward cache, but not on a first load', () => {
    const callback = vi.fn()
    const stop = onPageVisible(callback)

    pageShow(false)
    expect(callback).not.toHaveBeenCalled()

    pageShow(true)
    expect(callback).toHaveBeenCalledTimes(1)
    stop()
  })

  it('stops calling back once removed', () => {
    const callback = vi.fn()
    const stop = onPageVisible(callback)

    stop()
    document.dispatchEvent(new Event('visibilitychange'))
    pageShow(true)

    expect(callback).not.toHaveBeenCalled()
  })
})

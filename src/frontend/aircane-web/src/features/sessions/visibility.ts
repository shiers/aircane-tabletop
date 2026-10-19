/**
 * Runs `callback` whenever the page comes back in front of the player.
 *
 * A phone that is switched to another app suspends the page: its connections are cut and the server's
 * events sent in the meantime are lost for good. When the player returns, nothing tells the page that
 * it missed anything, so it has to look for itself. Two signals cover the ways back:
 *   - `visibilitychange` to "visible": the tab or app was brought forward again;
 *   - `pageshow` with `persisted`: the page was restored from the browser's back/forward cache
 *     (a first load also fires `pageshow`, but that is not a return, so it is ignored).
 *
 * Returns a function that removes both listeners.
 */
export function onPageVisible(callback: () => void): () => void {
  if (typeof document === 'undefined' || typeof window === 'undefined') return () => undefined

  const onVisibilityChange = () => {
    if (document.visibilityState === 'visible') callback()
  }
  const onPageShow = (event: PageTransitionEvent) => {
    if (event.persisted) callback()
  }

  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('pageshow', onPageShow)

  return () => {
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('pageshow', onPageShow)
  }
}

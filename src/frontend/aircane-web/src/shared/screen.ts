/**
 * Whether the browser window is narrower than Tailwind's `lg` breakpoint (1024px): a phone or a small
 * tablet. Used to choose what starts collapsed. Browsers that cannot report it (and jsdom) count as wide.
 */
export const NARROW_SCREEN_QUERY = '(max-width: 1023px)'

export function isNarrowScreen(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia(NARROW_SCREEN_QUERY).matches
  )
}

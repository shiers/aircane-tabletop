import { ref, computed, type Ref, type ComputedRef } from 'vue'

/**
 * Classification of an art asset by the kind of slot it fills. The type drives
 * which solid fallback colour is shown when the image is missing or fails to load.
 */
export type AssetType =
  | 'background'
  | 'hero'
  | 'panel'
  | 'button'
  | 'thumbnail'
  | 'ornament'
  | 'icon'
  | 'portrait'
  | 'provider-card'

export interface AssetOptions {
  path: string
  type: AssetType
  fallbackColor?: string
  fallbackEmoji?: string
}

/**
 * Solid fallback colours per asset type. Backgrounds/heros/panels/portraits/cards
 * get a dark navy fill; buttons/ornaments/icons stay transparent so their own
 * CSS base (or nothing, for ornaments) shows through.
 */
export const FALLBACK_COLORS: Record<AssetType, string> = {
  background: '#0a0a1a',
  hero: '#0d0d2a',
  panel: '#0d0d2a',
  button: 'transparent',
  thumbnail: '#0f0f1e',
  ornament: 'transparent',
  icon: 'transparent',
  portrait: '#0d0d2a',
  'provider-card': '#0d0d2a',
}

export interface UseAssetReturn {
  failed: Ref<boolean>
  onError: () => void
  backgroundStyle: ComputedRef<Record<string, string>>
  imgSrc: ComputedRef<string>
  fallbackEmoji?: string
}

/**
 * Graceful-degradation helper for a single art asset. Tracks a `failed` flag,
 * exposes an `onError` handler for `<img @error>`, and computes background/img
 * values that fall back to a solid colour (never throws, warns on failure).
 */
export function useAsset(options: AssetOptions): UseAssetReturn {
  const { path, type, fallbackColor, fallbackEmoji } = options
  const failed = ref(false)

  function onError(): void {
    failed.value = true
    console.warn(`[Aircane] Asset failed to load: ${path}`)
  }

  const backgroundStyle = computed<Record<string, string>>(() => {
    if (failed.value || type === 'ornament') return {}
    return {
      backgroundColor: fallbackColor ?? FALLBACK_COLORS[type],
      backgroundImage: failed.value ? 'none' : `url('${path}')`,
    }
  })

  const imgSrc = computed<string>(() => (failed.value ? '' : path))

  return { failed, onError, backgroundStyle, imgSrc, fallbackEmoji }
}

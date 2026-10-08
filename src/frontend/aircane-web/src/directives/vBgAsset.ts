import type { Directive, DirectiveBinding } from 'vue'

export interface BgAssetOptions {
  url: string
  fallback?: string
  size?: string
  position?: string
}

/**
 * Apply the solid fallback colour synchronously, then preload the image and
 * only paint `background-image` once it resolves. If the image fails, the
 * fallback colour remains and we warn (never throw). This avoids a flash of a
 * broken/empty background and leaves a clean dark fill when art is missing.
 */
function applyBgAsset(el: HTMLElement, options: BgAssetOptions): void {
  const { url, fallback = '#0a0a1a', size = 'cover', position = 'center' } = options

  // Fallback colour first, synchronously, so it shows immediately.
  el.style.backgroundColor = fallback
  // Remember the url we are loading so `updated` can skip redundant reloads.
  el.dataset.bgAssetUrl = url

  const img = new Image()
  img.onload = () => {
    el.style.backgroundImage = `url('${url}')`
    el.style.backgroundSize = size
    el.style.backgroundPosition = position
  }
  img.onerror = () => {
    console.warn(`[Aircane] Background asset failed to load: ${url}`)
  }
  img.src = url
}

export const vBgAsset: Directive<HTMLElement, BgAssetOptions> = {
  mounted(el: HTMLElement, binding: DirectiveBinding<BgAssetOptions>) {
    applyBgAsset(el, binding.value)
  },
  updated(el: HTMLElement, binding: DirectiveBinding<BgAssetOptions>) {
    if (binding.value.url !== binding.oldValue?.url) {
      applyBgAsset(el, binding.value)
    }
  },
}

export default vBgAsset

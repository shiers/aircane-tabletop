<script setup lang="ts">
/**
 * QRCode - renders a QR code for an arbitrary URL onto a canvas.
 *
 * Extracted so both the LAN join screen and the internet-play panel can encode
 * their respective URLs with one implementation.
 */
import { ref, onMounted, watch } from 'vue'
import QRCode from 'qrcode'

const props = withDefaults(
  defineProps<{
    /** The value to encode. */
    value: string
    /** Rendered width/height in pixels. */
    size?: number
    /** Foreground (module) colour. */
    dark?: string
    /** Background colour. */
    light?: string
  }>(),
  {
    size: 200,
    dark: '#ffffff',
    light: '#111827',
  },
)

const canvas = ref<HTMLCanvasElement | null>(null)
const error = ref<string | null>(null)

async function render(): Promise<void> {
  if (!canvas.value || !props.value) return
  error.value = null
  try {
    await QRCode.toCanvas(canvas.value, props.value, {
      width: props.size,
      margin: 2,
      color: { dark: props.dark, light: props.light },
    })
  } catch {
    error.value = 'Could not generate QR code.'
  }
}

onMounted(render)
watch(() => props.value, render)
</script>

<template>
  <div class="flex flex-col items-center gap-2">
    <canvas ref="canvas" aria-label="QR code" class="rounded-lg" />
    <p v-if="error" role="alert" class="text-xs text-red-400">{{ error }}</p>
  </div>
</template>

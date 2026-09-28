<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import AppLayout from '@/shared/components/AppLayout.vue'
import OllamaBanner from '@/shared/components/OllamaBanner.vue'
import { isDesktop, listen, TauriEvents } from '@/shared/tauri/bridge'

const router = useRouter()
const unlisteners: Array<() => void> = []

onMounted(async () => {
  if (!isDesktop()) return

  // Tray "Copy LAN URL" — the Rust side can't touch the clipboard, so it emits
  // the URL and we perform the copy here (the webview has clipboard access).
  unlisteners.push(
    await listen<string>(TauriEvents.CopyLanUrl, async (url) => {
      if (!url) return
      try {
        await navigator.clipboard.writeText(url)
      } catch {
        // Clipboard unavailable — ignore.
      }
    }),
  )

  // Deep-link from the wrapper (error page / Ollama banner) to AI settings.
  unlisteners.push(
    await listen(TauriEvents.NavigateAiSettings, () => {
      router.push({ name: 'ai-settings' })
    }),
  )
})

onUnmounted(() => {
  unlisteners.forEach((fn) => fn())
})
</script>

<template>
  <AppLayout>
    <OllamaBanner />
    <RouterView />
  </AppLayout>
</template>

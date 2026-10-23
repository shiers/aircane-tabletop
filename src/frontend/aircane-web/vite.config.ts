import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    // xfwd adds X-Forwarded-For/-Host/-Proto so the backend can see who the browser really is.
    // Without it every proxied request reaches the backend from 127.0.0.1, and a LAN player who
    // loads the app from this dev server (docs/setup/lan-hosting.md) would look like the developer
    // on the host machine. The backend trusts a proxied request only if it was forwarded for a
    // loopback client, so the developer's own browser still works without a token.
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        xfwd: true,
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        xfwd: true,
        ws: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: [],
    // The tests of the character features (and of the screens that show them) run with the Characters flag on,
    // as before it existed. The tests of the flag itself turn it on and off explicitly (`features.characters`).
    env: { VITE_ENABLE_CHARACTERS: 'true' },
  },
})

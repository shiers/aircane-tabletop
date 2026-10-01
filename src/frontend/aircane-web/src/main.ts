import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './App.vue'
import router from './router'
import './assets/main.css'
import { installErrorCapture, pushError } from './features/feedback/errorBuffer'

// Install console-error capture BEFORE creating the app so init-time errors are caught
// by the in-app feedback diagnostics.
installErrorCapture()

const app = createApp(App)

// Route Vue component errors into the same diagnostic buffer.
app.config.errorHandler = (err) => {
  pushError(err)
  // Preserve default behaviour: surface the error in the console.
  console.error(err)
}

app.use(createPinia())
app.use(router)

app.mount('#app')

import { ref } from 'vue'

/**
 * Shared, module-level open/closed state for the feedback modal so both the nav-bar button and
 * the Tauri tray listener (in App.vue) can open the same modal.
 */
const isOpen = ref(false)

export function useFeedbackModal() {
  function openFeedback(): void {
    isOpen.value = true
  }

  function closeFeedback(): void {
    isOpen.value = false
  }

  return { isOpen, openFeedback, closeFeedback }
}

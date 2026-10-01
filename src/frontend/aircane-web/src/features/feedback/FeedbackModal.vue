<script setup lang="ts">
/**
 * FeedbackModal — the in-app "Report a bug" dialog.
 *
 * The tester writes what went wrong; the app auto-captures diagnostic context, recent session
 * events (type + timestamp only), and recent console errors, then posts them to the backend,
 * which proxies a GitHub issue. Nothing sensitive (campaign content, character data, PDFs, API
 * keys) is captured or sent.
 */
import { computed, onMounted, ref } from 'vue'
import { submitFeedback } from './api'
import {
  useFeedbackDiagnostics,
  type DiagnosticContext,
  type DiagnosticCounts,
} from './composables/useFeedbackDiagnostics'
import { snapshot } from './composables/useFeedbackEventBuffer'
import { getConsoleErrors } from './errorBuffer'
import { isDesktop, openExternal } from '@/shared/tauri/bridge'

const emit = defineEmits<{ close: [] }>()

const { capture, counts } = useFeedbackDiagnostics()

// ── Form state ────────────────────────────────────────────────────────────────
const summary = ref('')
const description = ref('')
const stepsToReproduce = ref('')
const expectedBehaviour = ref('')

const showReproduce = ref(false)
const showDiagnostics = ref(true)
const showWhatsIncluded = ref(false)

// ── Submission state ────────────────────────────────────────────────────────────
type SubmitState = 'idle' | 'submitting' | 'success' | 'error'
const submitState = ref<SubmitState>('idle')
const issueUrl = ref<string | null>(null)
const issueNumber = ref<number | null>(null)

// ── Captured diagnostics (for display) ────────────────────────────────────────
const diagnostics = ref<DiagnosticContext | null>(null)
const diagnosticCounts = ref<DiagnosticCounts>({ recentEvents: 0, consoleErrors: 0 })

onMounted(async () => {
  diagnosticCounts.value = counts()
  try {
    diagnostics.value = await capture()
  } catch {
    diagnostics.value = null
  }
})

const canSubmit = computed(
  () =>
    summary.value.trim().length > 0 &&
    description.value.trim().length > 0 &&
    submitState.value !== 'submitting',
)

const aiProviderDisplay = computed(() => {
  const d = diagnostics.value
  if (!d || !d.aiProvider) return 'Unknown'
  return d.aiModel ? `${d.aiProvider} (${d.aiModel})` : d.aiProvider
})

const sessionDisplay = computed(() => {
  const id = diagnostics.value?.sessionId
  return id ? `${id} (active)` : 'none'
})

async function onSubmit(): Promise<void> {
  if (!canSubmit.value) return
  submitState.value = 'submitting'

  try {
    const context = diagnostics.value ?? (await capture())
    const result = await submitFeedback({
      summary: summary.value.trim(),
      description: description.value.trim(),
      stepsToReproduce: stepsToReproduce.value.trim() || null,
      expectedBehaviour: expectedBehaviour.value.trim() || null,
      diagnosticContext: context,
      recentEvents: snapshot(),
      consoleErrors: getConsoleErrors(),
    })
    issueUrl.value = result.issueUrl
    issueNumber.value = result.issueNumber
    submitState.value = 'success'
  } catch {
    submitState.value = 'error'
  }
}

function onCancel(): void {
  emit('close')
}

function openIssue(event: MouseEvent): void {
  // In the desktop wrapper, open the issue in the system browser rather than the webview.
  if (isDesktop() && issueUrl.value) {
    event.preventDefault()
    void openExternal(issueUrl.value)
  }
}
</script>

<template>
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
    role="dialog"
    aria-modal="true"
    aria-label="Report a bug"
  >
    <div class="flex max-h-[90vh] w-full max-w-lg flex-col overflow-hidden rounded-lg bg-surface-900 shadow-xl">
      <!-- Header -->
      <div class="flex items-center justify-between border-b border-surface-800 px-5 py-3">
        <h2 class="text-lg font-semibold text-white">Report a bug</h2>
        <button
          class="rounded-md p-1 text-gray-400 hover:bg-surface-800 hover:text-white"
          aria-label="Close"
          @click="onCancel"
        >
          ✕
        </button>
      </div>

      <!-- Body -->
      <div class="flex-1 overflow-y-auto px-5 py-4">
        <!-- Success state -->
        <div v-if="submitState === 'success'" class="text-center">
          <p class="mb-2 text-base text-emerald-400">Report submitted!</p>
          <p class="text-sm text-gray-300">
            Issue #{{ issueNumber }} —
            <a
              :href="issueUrl ?? '#'"
              target="_blank"
              rel="noopener noreferrer"
              class="text-sky-400 underline"
              @click="openIssue"
            >View on GitHub</a>
          </p>
        </div>

        <!-- Form -->
        <form v-else class="space-y-4" @submit.prevent="onSubmit">
          <!-- Section 1: What went wrong (required) -->
          <section class="space-y-3">
            <h3 class="text-sm font-medium text-gray-200">What went wrong</h3>
            <div>
              <label class="mb-1 block text-xs text-gray-400" for="feedback-summary">Summary *</label>
              <input
                id="feedback-summary"
                v-model="summary"
                type="text"
                required
                placeholder="One line describing the bug"
                class="w-full rounded-md border border-surface-700 bg-surface-800 px-3 py-2 text-sm text-white placeholder-gray-500"
              />
            </div>
            <div>
              <label class="mb-1 block text-xs text-gray-400" for="feedback-description">Description *</label>
              <textarea
                id="feedback-description"
                v-model="description"
                required
                rows="4"
                placeholder="What were you doing when it happened?"
                class="w-full rounded-md border border-surface-700 bg-surface-800 px-3 py-2 text-sm text-white placeholder-gray-500"
              ></textarea>
            </div>
          </section>

          <!-- Section 2: Help us reproduce (optional, collapsible) -->
          <section>
            <button
              type="button"
              class="flex items-center gap-1 text-sm font-medium text-gray-200"
              @click="showReproduce = !showReproduce"
            >
              <span>{{ showReproduce ? '▼' : '▶' }}</span>
              Help us reproduce (optional)
            </button>
            <div v-if="showReproduce" class="mt-3 space-y-3">
              <div>
                <label class="mb-1 block text-xs text-gray-400" for="feedback-steps">Steps to reproduce</label>
                <textarea
                  id="feedback-steps"
                  v-model="stepsToReproduce"
                  rows="3"
                  placeholder="1. ...&#10;2. ..."
                  class="w-full rounded-md border border-surface-700 bg-surface-800 px-3 py-2 text-sm text-white placeholder-gray-500"
                ></textarea>
              </div>
              <div>
                <label class="mb-1 block text-xs text-gray-400" for="feedback-expected">Expected behaviour</label>
                <textarea
                  id="feedback-expected"
                  v-model="expectedBehaviour"
                  rows="2"
                  placeholder="What should have happened?"
                  class="w-full rounded-md border border-surface-700 bg-surface-800 px-3 py-2 text-sm text-white placeholder-gray-500"
                ></textarea>
              </div>
            </div>
          </section>

          <!-- Section 3: Diagnostic context (always shown, read-only, expanded by default) -->
          <section class="rounded-md border border-surface-800 bg-surface-950/50 p-3">
            <button
              type="button"
              class="flex items-center gap-1 text-sm font-medium text-gray-200"
              @click="showDiagnostics = !showDiagnostics"
            >
              <span>{{ showDiagnostics ? '▼' : '▶' }}</span>
              Diagnostic context (auto-captured)
            </button>
            <dl v-if="showDiagnostics" class="mt-3 space-y-1 text-xs text-gray-300" data-testid="diagnostic-context">
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">Platform</dt>
                <dd>{{ diagnostics?.platform ?? '-' }}{{ diagnostics?.isTauri ? ` / Tauri ${diagnostics?.tauriVersion ?? ''}` : '' }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">App version</dt>
                <dd>{{ diagnostics?.appVersion ?? '-' }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">AI provider</dt>
                <dd>{{ aiProviderDisplay }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">Active screen</dt>
                <dd>{{ diagnostics?.activeRoute ?? '-' }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">Session</dt>
                <dd>{{ sessionDisplay }}</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">Recent events</dt>
                <dd>{{ diagnosticCounts.recentEvents }} captured</dd>
              </div>
              <div class="flex justify-between gap-2">
                <dt class="text-gray-500">Console errors</dt>
                <dd>{{ diagnosticCounts.consoleErrors }} captured</dd>
              </div>

              <div class="pt-2">
                <button
                  type="button"
                  class="text-sky-400 underline"
                  @click="showWhatsIncluded = !showWhatsIncluded"
                >
                  What's included?
                </button>
                <p v-if="showWhatsIncluded" class="mt-1 text-gray-400">
                  This report includes the details above plus the last few session events (type and
                  time only) and recent console errors. It does <strong>not</strong> include your
                  campaign content, character data, or PDFs.
                </p>
              </div>
            </dl>
          </section>

          <!-- Error state -->
          <p v-if="submitState === 'error'" class="text-sm text-red-400">
            Could not submit — please try again or post in Discord.
          </p>
        </form>
      </div>

      <!-- Footer -->
      <div class="flex items-center justify-between border-t border-surface-800 px-5 py-3">
        <button
          type="button"
          class="rounded-md px-3 py-1.5 text-sm text-gray-300 hover:bg-surface-800"
          @click="onCancel"
        >
          {{ submitState === 'success' ? 'Close' : 'Cancel' }}
        </button>
        <button
          v-if="submitState !== 'success'"
          type="button"
          :disabled="!canSubmit"
          class="flex items-center gap-2 rounded-md bg-sky-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-sky-500 disabled:cursor-not-allowed disabled:opacity-50"
          @click="onSubmit"
        >
          <span
            v-if="submitState === 'submitting'"
            class="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white border-t-transparent"
            aria-hidden="true"
          ></span>
          {{ submitState === 'submitting' ? 'Submitting…' : 'Submit report' }}
        </button>
      </div>
    </div>
  </div>
</template>

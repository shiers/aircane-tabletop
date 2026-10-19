import type { Router } from 'vue-router'
import { getAdventureSession } from './api'

/**
 * Opens an adventure run that is already in progress, on the right screen: a group run goes to the
 * host's session screen (where the run view lives), a solo run to the solo screen. The cards that
 * offer "Resume" only know the run's id, so the run is read first. If it cannot be read, fall back
 * to the solo screen, which shows the problem.
 */
export async function openRun(router: Router, runId: string): Promise<void> {
  try {
    const run = await getAdventureSession(runId)
    if (run.isGroup && run.sessionId) {
      await router.push({ name: 'host-session', params: { sessionId: run.sessionId } })
      return
    }
  } catch {
    // Fall through to the solo screen.
  }
  await router.push({ name: 'adventure-solo', params: { sessionId: runId } })
}

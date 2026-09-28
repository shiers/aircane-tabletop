import { defineStore } from 'pinia'
import { ref } from 'vue'
import * as signalR from '@microsoft/signalr'
import {
  listDocuments,
  uploadDocument,
  deleteDocument,
  reindexDocument,
  setDocumentDisabled,
  restoreBuiltInDefaults,
  getImportStatus,
  getFolders,
  registerFolder,
  deleteFolder,
  scanFolder,
  previewFolderScan,
  importFolderSelection,
  ImportStatus,
  type SourceDocumentDto,
  type ImportStatusDto,
  type ListDocumentsParams,
  type UploadDocumentRequest,
  type WatchedFolderDto,
  type RegisterFolderRequest,
  type FolderScanResultDto,
  type FolderScanPreviewDto,
  type FolderImportSelectionItem,
  type FolderScanResultSummaryDto,
} from './api'

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const HUB_URL = '/hubs/library'
/** Fallback polling interval used only when SignalR is unavailable. */
const POLL_INTERVAL_MS = 3_000

// ---------------------------------------------------------------------------
// Helpers (module-level to avoid linter warnings)
// ---------------------------------------------------------------------------

function isNonTerminal(status: ImportStatus): boolean {
  return status === ImportStatus.Pending || status === ImportStatus.Processing
}

function extractMessage(err: unknown): string {
  if (err instanceof Error) return err.message
  return 'An unexpected error occurred.'
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useLibraryStore = defineStore('library', () => {
  // -------------------------------------------------------------------------
  // State
  // -------------------------------------------------------------------------
  const documents = ref<SourceDocumentDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const uploadProgress = ref<number>(0)

  /** Map of documentId → latest ImportStatusDto for in-flight documents */
  const statusMap = ref<Record<string, ImportStatusDto>>({})

  /** Watched folders registered by the host */
  const folders = ref<WatchedFolderDto[]>([])
  const foldersLoading = ref(false)
  const foldersError = ref<string | null>(null)

  /** Map of folderId → scan result for the most recent scan */
  const scanResultMap = ref<Record<string, FolderScanResultDto>>({})

  /** Set of folderIds currently being scanned */
  const scanningFolderIds = ref<Set<string>>(new Set())

  /** Active fallback polling timer handles keyed by documentId */
  const pollTimers: Record<string, ReturnType<typeof setInterval>> = {}

  /** SignalR connection instance */
  let hubConnection: signalR.HubConnection | null = null

  // -------------------------------------------------------------------------
  // SignalR
  // -------------------------------------------------------------------------

  /** Build and start the SignalR connection, then join the "library" group. */
  async function connectSignalR(): Promise<void> {
    if (hubConnection) return // already connected or connecting

    hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    hubConnection.on('ImportStatusUpdated', (status: ImportStatusDto) => {
      handleImportStatusUpdated(status)
    })

    try {
      await hubConnection.start()
      await hubConnection.invoke('JoinLibraryGroup')
    } catch (err) {
      // SignalR unavailable - fall back to polling for non-terminal documents
      console.warn('[LibraryStore] SignalR connection failed, falling back to polling.', err)
      hubConnection = null
      for (const doc of documents.value) {
        if (isNonTerminal(doc.importStatus)) {
          startPolling(doc.id)
        }
      }
    }
  }

  /** Stop the SignalR connection and clean up. */
  async function disconnectSignalR(): Promise<void> {
    if (!hubConnection) return
    try {
      await hubConnection.invoke('LeaveLibraryGroup')
      await hubConnection.stop()
    } catch {
      // Best-effort cleanup
    } finally {
      hubConnection = null
    }
  }

  /** Handle an incoming ImportStatusUpdated event from the hub. */
  function handleImportStatusUpdated(status: ImportStatusDto): void {
    statusMap.value[status.documentId] = status

    const doc = documents.value.find((d) => d.id === status.documentId)
    if (doc) {
      doc.importStatus = status.status
    }

    // Stop any fallback polling for this document once we receive a terminal status
    if (!isNonTerminal(status.status)) {
      stopPolling(status.documentId)
    }
  }

  // -------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------

  /** Fetch the full document list, optionally filtered. */
  async function fetchDocuments(params?: ListDocumentsParams): Promise<void> {
    loading.value = true
    error.value = null
    try {
      documents.value = await listDocuments(params)

      // Ensure SignalR is connected so we receive live updates (non-blocking)
      connectSignalR().catch(() => {
        // SignalR failure is non-critical; polling handles fallback
      })

      // If SignalR is unavailable, fall back to polling for non-terminal docs
      if (!hubConnection) {
        for (const doc of documents.value) {
          if (isNonTerminal(doc.importStatus)) {
            startPolling(doc.id)
          }
        }
      }
    } catch (err) {
      error.value = extractMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Upload a new document and add it to the list. */
  async function uploadDocumentAction(request: UploadDocumentRequest): Promise<void> {
    loading.value = true
    error.value = null
    uploadProgress.value = 0
    try {
      const doc = await uploadDocument(request, (pct) => {
        uploadProgress.value = pct
      })
      documents.value.unshift(doc)

      // Ensure SignalR is connected; fall back to polling if unavailable
      await connectSignalR()
      if (!hubConnection) {
        startPolling(doc.id)
      }
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    } finally {
      loading.value = false
      uploadProgress.value = 0
    }
  }

  /** Delete a document and remove it from the list. */
  async function deleteDocumentAction(id: string): Promise<void> {
    error.value = null
    try {
      await deleteDocument(id)
      stopPolling(id)
      documents.value = documents.value.filter((d) => d.id !== id)
      delete statusMap.value[id]
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Re-run the import job for a document. */
  async function reindexDocumentAction(id: string): Promise<void> {
    error.value = null
    try {
      await reindexDocument(id)
      // Optimistically mark as pending
      const doc = documents.value.find((d) => d.id === id)
      if (doc) doc.importStatus = ImportStatus.Pending

      // Ensure SignalR is connected; fall back to polling if unavailable
      await connectSignalR()
      if (!hubConnection) {
        startPolling(id)
      }
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Enable or disable a document (used for built-in content that cannot be deleted). */
  async function setDocumentDisabledAction(id: string, disabled: boolean): Promise<void> {
    error.value = null
    try {
      const updated = await setDocumentDisabled(id, disabled)
      const doc = documents.value.find((d) => d.id === id)
      if (doc) doc.isDisabled = updated.isDisabled
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Re-enable all disabled built-in documents, then refresh the list. */
  async function restoreBuiltInDefaultsAction(): Promise<number> {
    error.value = null
    try {
      const result = await restoreBuiltInDefaults()
      if (result.restoredCount > 0) {
        // Reflect the change locally without a full refetch.
        for (const doc of documents.value) {
          if (doc.isBuiltIn && doc.isDisabled) doc.isDisabled = false
        }
      }
      return result.restoredCount
    } catch (err) {
      error.value = extractMessage(err)
      throw err
    }
  }

  /** Poll the import status for a single document once (fallback when SignalR is unavailable). */
  async function pollImportStatus(id: string): Promise<void> {
    try {
      const status = await getImportStatus(id)
      statusMap.value[id] = status

      const doc = documents.value.find((d) => d.id === id)
      if (doc) {
        doc.importStatus = status.status
      }

      if (!isNonTerminal(status.status)) {
        stopPolling(id)
      }
    } catch {
      // Silently ignore transient polling errors
    }
  }

  // -------------------------------------------------------------------------
  // Polling helpers (fallback only)
  // -------------------------------------------------------------------------

  function startPolling(id: string): void {
    if (pollTimers[id]) return
    pollTimers[id] = setInterval(() => pollImportStatus(id), POLL_INTERVAL_MS)
  }

  function stopPolling(id: string): void {
    if (pollTimers[id]) {
      clearInterval(pollTimers[id])
      delete pollTimers[id]
    }
  }

  function stopAllPolling(): void {
    for (const id of Object.keys(pollTimers)) {
      stopPolling(id)
    }
  }

  /** Tear down SignalR and polling - call on store/component cleanup. */
  async function cleanup(): Promise<void> {
    stopAllPolling()
    await disconnectSignalR()
  }

  // -------------------------------------------------------------------------
  // Folder actions
  // -------------------------------------------------------------------------

  /** Fetch all registered watched folders. */
  async function fetchFolders(): Promise<void> {
    foldersLoading.value = true
    foldersError.value = null
    try {
      folders.value = await getFolders()
    } catch (err) {
      foldersError.value = extractMessage(err)
    } finally {
      foldersLoading.value = false
    }
  }

  /** Register a new watched folder. */
  async function registerFolderAction(request: RegisterFolderRequest): Promise<void> {
    foldersError.value = null
    try {
      const folder = await registerFolder(request)
      folders.value.push(folder)
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    }
  }

  /** Unregister a watched folder and remove its documents from the list. */
  async function deleteFolderAction(id: string): Promise<void> {
    foldersError.value = null
    try {
      await deleteFolder(id)
      folders.value = folders.value.filter((f) => f.id !== id)
      // Remove documents that belonged to this folder
      documents.value = documents.value.filter((d) => d.watchedFolderId !== id)
      delete scanResultMap.value[id]
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    }
  }

  /** Trigger a manual rescan of a folder and refresh the document list. */
  async function scanFolderAction(id: string): Promise<void> {
    foldersError.value = null
    const ids = new Set(scanningFolderIds.value)
    ids.add(id)
    scanningFolderIds.value = ids
    try {
      const result = await scanFolder(id)
      scanResultMap.value[id] = result

      // Update lastScannedAt on the folder record
      const folder = folders.value.find((f) => f.id === id)
      if (folder) {
        folder.lastScannedAt = result.scannedAt
      }

      // Refresh documents to pick up newly discovered files
      await fetchDocuments()
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    } finally {
      const ids2 = new Set(scanningFolderIds.value)
      ids2.delete(id)
      scanningFolderIds.value = ids2
    }
  }

  /**
   * Analyze a folder and return a review preview without importing anything. Marks the folder as
   * scanning while the analysis runs so the UI can show progress.
   */
  async function previewFolderScanAction(id: string): Promise<FolderScanPreviewDto> {
    foldersError.value = null
    const ids = new Set(scanningFolderIds.value)
    ids.add(id)
    scanningFolderIds.value = ids
    try {
      return await previewFolderScan(id)
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    } finally {
      const ids2 = new Set(scanningFolderIds.value)
      ids2.delete(id)
      scanningFolderIds.value = ids2
    }
  }

  /**
   * Option B "Quick import": analyze the folder and import every candidate that has NO advisory
   * flags (i.e. not a duplicate variant, not already imported, not likely-non-rules), using the
   * suggested title/ruleset. Flagged files are intentionally left for the review modal — we never
   * auto-pick a duplicate winner. Returns the import summary plus how many flagged files were
   * skipped so the caller can nudge the user toward the review flow.
   */
  async function quickImportFolderAction(
    id: string,
  ): Promise<{ result: FolderScanResultSummaryDto; skippedFlagged: number }> {
    foldersError.value = null
    const ids = new Set(scanningFolderIds.value)
    ids.add(id)
    scanningFolderIds.value = ids
    try {
      const preview = await previewFolderScan(id)

      const items: FolderImportSelectionItem[] = preview.candidates.map((c) => {
        const clean = c.flags.length === 0
        return {
          sourcePath: c.sourcePath,
          import: clean,
          title: c.suggestedTitle || undefined,
          ruleset: c.suggestedRuleset ?? undefined,
        }
      })

      const skippedFlagged = preview.candidates.filter((c) => c.flags.length > 0).length
      const result = await importFolderSelection(id, items)

      const folder = folders.value.find((f) => f.id === id)
      if (folder) folder.lastScannedAt = result.scannedAt
      await fetchDocuments()

      return { result, skippedFlagged }
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    } finally {
      const ids2 = new Set(scanningFolderIds.value)
      ids2.delete(id)
      scanningFolderIds.value = ids2
    }
  }

  /**
   * Import the host-selected files from a folder-scan preview, then refresh the document list and
   * the folder's lastScannedAt.
   */
  async function importFolderSelectionAction(
    id: string,
    items: FolderImportSelectionItem[],
  ): Promise<FolderScanResultSummaryDto> {
    foldersError.value = null
    try {
      const result = await importFolderSelection(id, items)

      const folder = folders.value.find((f) => f.id === id)
      if (folder) {
        folder.lastScannedAt = result.scannedAt
      }

      // Refresh documents to pick up the newly imported files.
      await fetchDocuments()
      return result
    } catch (err) {
      foldersError.value = extractMessage(err)
      throw err
    }
  }

  return {
    // State
    documents,
    loading,
    error,
    uploadProgress,
    statusMap,
    folders,
    foldersLoading,
    foldersError,
    scanResultMap,
    scanningFolderIds,
    // Actions
    fetchDocuments,
    uploadDocument: uploadDocumentAction,
    deleteDocument: deleteDocumentAction,
    reindexDocument: reindexDocumentAction,
    setDocumentDisabled: setDocumentDisabledAction,
    restoreBuiltInDefaults: restoreBuiltInDefaultsAction,
    pollImportStatus,
    stopAllPolling,
    cleanup,
    fetchFolders,
    registerFolder: registerFolderAction,
    deleteFolder: deleteFolderAction,
    scanFolder: scanFolderAction,
    previewFolderScan: previewFolderScanAction,
    importFolderSelection: importFolderSelectionAction,
    quickImportFolder: quickImportFolderAction,
  }
})

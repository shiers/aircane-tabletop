import apiClient from '@/shared/api/client'
import type { AxiosProgressEvent } from 'axios'

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

export enum SourceType {
  Unknown = 0,
  Rules = 1,
  Adventure = 2,
  Solo = 3,
  Character = 4,
  Homebrew = 5,
  Generated = 6,
}

export enum ImportStatus {
  Pending = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
  OcrRequired = 4,
}

export enum ContentVisibility {
  Public = 0,
  DMOnly = 1,
  Hidden = 2,
  Revealed = 3,
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

export interface WatchedFolderDto {
  id: string
  displayName: string
  absolutePath: string
  defaultSourceType: SourceType
  defaultGameSystem: string | null
  defaultRuleset: string | null
  lastScannedAt: string | null
  createdAt: string
}

export interface RegisterFolderRequest {
  displayName: string
  absolutePath: string
  defaultSourceType: SourceType
  defaultGameSystem?: string
  defaultRuleset?: string
}

export interface UpdateFolderRequest {
  displayName?: string
  defaultSourceType?: SourceType
  defaultGameSystem?: string
  defaultRuleset?: string
}

export interface FolderScanResultDto {
  folderId: string
  scannedAt: string
  newDocuments: number
  updatedDocuments: number
  unchangedDocuments: number
  errors: string[]
}

export interface SourceDocumentDto {
  id: string
  title: string
  originalFileName: string
  sourceType: SourceType
  gameSystem: string | null
  ruleset: string | null
  visibility: ContentVisibility
  importStatus: ImportStatus
  isSourceAvailable: boolean
  watchedFolderId: string | null
  createdAt: string
  updatedAt: string
}

export interface ImportStatusDto {
  documentId: string
  status: ImportStatus
  progressPercent: number
  errorMessage: string | null
  updatedAt: string
}

export interface ListDocumentsParams {
  sourceType?: SourceType
  gameSystem?: string
  ruleset?: string
  importStatus?: ImportStatus
  page?: number
  pageSize?: number
}

export interface UploadDocumentRequest {
  file: File
  title: string
  sourceType: SourceType
  gameSystem?: string
  ruleset?: string
  visibility?: ContentVisibility
}

export interface UpdateClassificationRequest {
  sourceType?: SourceType
  gameSystem?: string
  ruleset?: string
  visibility?: ContentVisibility
}

// ---------------------------------------------------------------------------
// API functions
// ---------------------------------------------------------------------------

/**
 * Upload a new document to the library.
 * Reports upload progress via the optional onUploadProgress callback.
 */
export async function uploadDocument(
  request: UploadDocumentRequest,
  onUploadProgress?: (progressPercent: number) => void,
): Promise<SourceDocumentDto> {
  const form = new FormData()
  form.append('file', request.file)
  form.append('title', request.title)
  form.append('sourceType', String(request.sourceType))
  if (request.gameSystem) form.append('gameSystem', request.gameSystem)
  if (request.ruleset) form.append('ruleset', request.ruleset)
  if (request.visibility !== undefined) form.append('visibility', String(request.visibility))

  const response = await apiClient.post<SourceDocumentDto>('/api/library/documents', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (event: AxiosProgressEvent) => {
      if (onUploadProgress && event.total) {
        onUploadProgress(Math.round((event.loaded * 100) / event.total))
      }
    },
  })
  return response.data
}

/** List documents with optional filters. */
export async function listDocuments(params?: ListDocumentsParams): Promise<SourceDocumentDto[]> {
  const response = await apiClient.get<SourceDocumentDto[]>('/api/library/documents', { params })
  return response.data
}

/** Get a single document by ID. */
export async function getDocument(id: string): Promise<SourceDocumentDto> {
  const response = await apiClient.get<SourceDocumentDto>(`/api/library/documents/${id}`)
  return response.data
}

/** Delete a document by ID. */
export async function deleteDocument(id: string): Promise<void> {
  await apiClient.delete(`/api/library/documents/${id}`)
}

/** Get the import status for a document. */
export async function getImportStatus(id: string): Promise<ImportStatusDto> {
  const response = await apiClient.get<ImportStatusDto>(`/api/library/documents/${id}/status`)
  return response.data
}

/** Re-run the import job for a document. */
export async function reindexDocument(id: string): Promise<void> {
  await apiClient.post(`/api/library/documents/${id}/reindex`)
}

/** Update the classification metadata for a document. */
export async function updateClassification(
  id: string,
  request: UpdateClassificationRequest,
): Promise<SourceDocumentDto> {
  const response = await apiClient.patch<SourceDocumentDto>(
    `/api/library/documents/${id}/classification`,
    request,
  )
  return response.data
}

// ---------------------------------------------------------------------------
// Folder API functions
// ---------------------------------------------------------------------------

/** List all registered watched folders. */
export async function getFolders(): Promise<WatchedFolderDto[]> {
  const response = await apiClient.get<WatchedFolderDto[]>('/api/library/folders')
  return response.data
}

/** Register a new watched folder. */
export async function registerFolder(request: RegisterFolderRequest): Promise<WatchedFolderDto> {
  const response = await apiClient.post<WatchedFolderDto>('/api/library/folders', request)
  return response.data
}

/** Update settings for a watched folder. */
export async function updateFolder(
  id: string,
  request: UpdateFolderRequest,
): Promise<WatchedFolderDto> {
  const response = await apiClient.put<WatchedFolderDto>(`/api/library/folders/${id}`, request)
  return response.data
}

/** Unregister a watched folder (does not delete source files). */
export async function deleteFolder(id: string): Promise<void> {
  await apiClient.delete(`/api/library/folders/${id}`)
}

/** Trigger a manual rescan of a watched folder. */
export async function scanFolder(id: string): Promise<FolderScanResultDto> {
  const response = await apiClient.post<FolderScanResultDto>(
    `/api/library/folders/${id}/scan`,
  )
  return response.data
}

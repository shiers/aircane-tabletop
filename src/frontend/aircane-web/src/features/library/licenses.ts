import apiClient from '@/shared/api/client'

/**
 * License and attribution metadata for a built-in rules content document.
 * Mirrors the backend LicenseInfoDto. Served publicly (no auth) so license
 * obligations can be fulfilled by any client.
 */
export interface LicenseInfo {
  documentId: string
  documentTitle: string
  licenseKey: string | null
  licenseDisplayName: string | null
  licenseUrl: string | null
  attributionText: string | null
  attributionUrl: string | null
  isBuiltIn: boolean
  isOgl: boolean
}

/** List license/attribution metadata for all built-in documents. Public, no auth. */
export async function listLicenses(): Promise<LicenseInfo[]> {
  const response = await apiClient.get<LicenseInfo[]>('/api/library/licenses')
  return response.data
}

/** Fetch the verbatim OGL v1.0a license text for an OGL document (text/plain). */
export async function getOglText(documentId: string): Promise<string> {
  const response = await apiClient.get<string>(
    `/api/library/licenses/${documentId}/ogl-text`,
    { responseType: 'text' },
  )
  return response.data
}

/** Fetch the verbatim Section 15 attribution chain for an OGL document (text/plain). */
export async function getSection15(documentId: string): Promise<string> {
  const response = await apiClient.get<string>(
    `/api/library/licenses/${documentId}/section-15`,
    { responseType: 'text' },
  )
  return response.data
}

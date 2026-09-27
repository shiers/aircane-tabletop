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
}

/** List license/attribution metadata for all built-in documents. Public, no auth. */
export async function listLicenses(): Promise<LicenseInfo[]> {
  const response = await apiClient.get<LicenseInfo[]>('/api/library/licenses')
  return response.data
}

export interface ThumbnailBuildPaths {
  sourceDir: string
  metaFile: string
  outDir: string
  manifestFile: string
}
export interface ThumbnailBuildOptions {
  force?: boolean
  log?: (msg: string) => void
  warn?: (msg: string) => void
}
export interface ThumbnailBuildResult {
  conversion: { converted: string[]; skipped: string[]; flagged: string[] }
  manifest: { entries: unknown[]; changed: boolean }
}
export const defaultPaths: ThumbnailBuildPaths
export const TARGET_WIDTH: number
export const WEBP_QUALITY: number
export function run(paths?: ThumbnailBuildPaths, opts?: ThumbnailBuildOptions): Promise<ThumbnailBuildResult>

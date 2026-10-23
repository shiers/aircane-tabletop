/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** "true" shows the Characters feature (see src/shared/features.ts). Unset or anything else hides it. */
  readonly VITE_ENABLE_CHARACTERS?: string
}

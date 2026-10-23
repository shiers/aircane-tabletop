/**
 * Guards the type-check against silently checking nothing.
 *
 * `vue-tsc` with no arguments reads tsconfig.json, which is a solution-style root (`"files": []` plus references):
 * it lists no files and follows no references, so it passed every time while checking nothing, and a stray `}` in a
 * .vue file got through to Vite. `vue-tsc -b` does walk the references, but only if they are there. This script asks
 * vue-tsc which files each referenced project really contains (`--listFilesOnly`, which is what the checker sees,
 * .vue files included) and fails unless the list has the things that must always be checked:
 *
 *   - the app entry point (src/main.ts) and a .vue component,
 *   - a vitest spec,
 *   - vite.config.ts,
 *   - a Playwright e2e spec.
 *
 * `npm run typecheck` runs it before `vue-tsc -b`. Plain Node, no shell, so it runs the same on Windows.
 */
import { execFile } from 'node:child_process'
import { existsSync, statSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { promisify } from 'node:util'

const execFileAsync = promisify(execFile)

/** What the type-check must cover. `test` receives a normalised absolute path (forward slashes). */
export const REQUIRED = [
  { what: 'the app entry point (src/main.ts)', test: (file) => file.endsWith('/src/main.ts') },
  { what: 'a Vue component (src/**/*.vue)', test: (file) => /\/src\/.+\.vue$/.test(file) },
  { what: 'a vitest spec (src/**/__tests__/*.test.ts)', test: (file) => /\/src\/.+\/__tests__\/[^/]+\.test\.ts$/.test(file) },
  { what: 'vite.config.ts', test: (file) => file.endsWith('/vite.config.ts') },
  {
    what: 'a Playwright e2e spec (tests/e2e/playwright/tests/*.spec.ts)',
    test: (file) => /\/tests\/e2e\/playwright\/tests\/[^/]+\.spec\.ts$/.test(file),
  },
]

export function normalize(file) {
  return file.trim().replace(/\\/g, '/')
}

/** The repo's own files in `files`: normalised, without blanks and without anything under node_modules. */
export function ownFiles(files) {
  return [...new Set(files.map(normalize).filter((file) => file && !file.includes('/node_modules/')))]
}

/** The required things that no file in `files` provides. Files under node_modules never count. */
export function findMissing(files) {
  const own = ownFiles(files)
  return REQUIRED.filter((req) => !own.some(req.test)).map((req) => req.what)
}

/**
 * The tsconfig files a root tsconfig references, as absolute paths. Throws when it references nothing: building
 * such a root checks nothing, which is the failure this script exists to catch.
 */
export function referencedProjects(rootConfigPath, ts) {
  const { config, error } = ts.readConfigFile(rootConfigPath, ts.sys.readFile)
  if (error) throw new Error(`Cannot read ${rootConfigPath}: ${ts.flattenDiagnosticMessageText(error.messageText, '\n')}`)
  const references = config.references ?? []
  if (references.length === 0) {
    throw new Error(`${rootConfigPath} has no "references", so \`vue-tsc -b\` would check nothing.`)
  }
  const rootDir = path.dirname(rootConfigPath)
  return references.map((ref) => {
    const target = path.resolve(rootDir, ref.path)
    return existsSync(target) && statSync(target).isDirectory() ? path.join(target, 'tsconfig.json') : target
  })
}

/** The files vue-tsc would check for one project (absolute paths, forward slashes). */
async function listProjectFiles(vueTscBin, project) {
  try {
    const { stdout } = await execFileAsync(process.execPath, [vueTscBin, '--listFilesOnly', '-p', project], {
      maxBuffer: 64 * 1024 * 1024,
    })
    return stdout.split(/\r?\n/).map(normalize).filter(Boolean)
  } catch (error) {
    // vue-tsc prints its diagnostics (a syntax error, a bad tsconfig) and then the file list; show only the former.
    const output = [error.stdout, error.stderr].filter(Boolean).join('\n')
    const diagnostics = output.split(/\r?\n/).filter((line) => line.includes('error TS'))
    throw new Error(`vue-tsc could not read ${project}:\n${diagnostics.join('\n') || output.trim() || error.message}`)
  }
}

export async function main(frontendDir) {
  const require = createRequire(path.join(frontendDir, 'package.json'))
  const ts = require('typescript')
  const vueTscBin = require.resolve('vue-tsc/bin/vue-tsc.js')

  const projects = referencedProjects(path.join(frontendDir, 'tsconfig.json'), ts)
  const lists = await Promise.all(projects.map((project) => listProjectFiles(vueTscBin, project)))
  const files = lists.flat()

  const missing = findMissing(files)
  if (missing.length > 0) {
    console.error('The type-check does not cover everything it must. Not checked:')
    for (const what of missing) console.error(`  - ${what}`)
    console.error(`Projects walked (from tsconfig.json "references"):\n${projects.map((p) => `  ${normalize(p)}`).join('\n')}`)
    console.error('Fix the "include" of those projects, or add the missing project to "references" in tsconfig.json.')
    return 1
  }
  console.log(`Type-check coverage OK: ${ownFiles(files).length} of the repo's files in ${projects.length} projects.`)
  return 0
}

const isMain = process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url
if (isMain) {
  const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
  main(frontendDir).then(
    (code) => process.exit(code),
    (error) => {
      console.error(error.message)
      process.exit(1)
    },
  )
}

import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { findMissing, normalize, ownFiles, referencedProjects, REQUIRED } from './check-typecheck-coverage.mjs'

const require = createRequire(import.meta.url)
const ts = require('typescript')

const ROOT = 'D:/work/aircane-tabletop'
const FRONTEND = `${ROOT}/src/frontend/aircane-web`
const COMPLETE = [
  `${FRONTEND}/src/main.ts`,
  `${FRONTEND}/src/App.vue`,
  `${FRONTEND}/src/features/sessions/__tests__/PlayerSessionView.test.ts`,
  `${FRONTEND}/vite.config.ts`,
  `${ROOT}/tests/e2e/playwright/tests/navigation.spec.ts`,
]

describe('findMissing', () => {
  it('finds nothing missing when the app, a component, a vitest spec, vite.config.ts and an e2e spec are all listed', () => {
    expect(findMissing(COMPLETE)).toEqual([])
  })

  it('reports everything missing from an empty list, which is what a root with `files: []` lists', () => {
    expect(findMissing([])).toHaveLength(REQUIRED.length)
  })

  it.each([
    ['src/main.ts', 'src/main.ts'],
    ['a .vue component', '.vue'],
    ['a vitest spec', '.test.ts'],
    ['vite.config.ts', 'vite.config.ts'],
    ['an e2e spec', '.spec.ts'],
  ])('reports %s when it is the only thing not listed', (_label, marker) => {
    const missing = findMissing(COMPLETE.filter((file) => !file.endsWith(marker)))

    expect(missing).toHaveLength(1)
  })

  it('does not count a main.ts, component or spec that lives under node_modules', () => {
    const files = [
      `${FRONTEND}/node_modules/some-lib/src/main.ts`,
      `${FRONTEND}/node_modules/some-lib/src/Widget.vue`,
      `${FRONTEND}/node_modules/some-lib/src/__tests__/widget.test.ts`,
      `${FRONTEND}/node_modules/some-lib/vite.config.ts`,
      `${ROOT}/tests/e2e/playwright/node_modules/x/tests/y.spec.ts`,
    ]

    expect(findMissing(files)).toHaveLength(REQUIRED.length)
  })

  it('does not count an e2e-looking spec outside tests/e2e/playwright/tests, or a spec that is not a vitest spec', () => {
    const files = [`${FRONTEND}/src/features/x/__tests__/helper.ts`, `${ROOT}/tests/backend/other.spec.ts`]

    expect(findMissing(files)).toHaveLength(REQUIRED.length)
  })
})

describe('normalize and ownFiles', () => {
  it('turns Windows paths into forward-slash paths, so the checks behave the same on every platform', () => {
    expect(normalize('D:\\work\\aircane\\src\\main.ts\r')).toBe('D:/work/aircane/src/main.ts')
    expect(findMissing(COMPLETE.map((file) => file.replace(/\//g, '\\')))).toEqual([])
  })

  it('drops blanks, node_modules files and duplicates', () => {
    expect(ownFiles(['', `${FRONTEND}/src/main.ts`, `${FRONTEND}/src/main.ts`, `${FRONTEND}/node_modules/a/b.d.ts`])).toEqual([
      `${FRONTEND}/src/main.ts`,
    ])
  })
})

describe('referencedProjects', () => {
  let dir

  beforeEach(() => {
    dir = mkdtempSync(path.join(os.tmpdir(), 'typecheck-coverage-'))
  })
  afterEach(() => {
    rmSync(dir, { recursive: true, force: true })
  })

  const write = (relative, content) => {
    const file = path.join(dir, relative)
    mkdirSync(path.dirname(file), { recursive: true })
    writeFileSync(file, content)
    return file
  }

  it('lists the referenced tsconfig files, resolving a folder to its tsconfig.json', () => {
    write('app/tsconfig.app.json', '{}')
    write('e2e/tsconfig.json', '{}')
    const root = write('app/tsconfig.json', '{ "files": [], "references": [ { "path": "./tsconfig.app.json" }, { "path": "../e2e" } ] }')

    expect(referencedProjects(root, ts)).toEqual([path.join(dir, 'app', 'tsconfig.app.json'), path.join(dir, 'e2e', 'tsconfig.json')])
  })

  it('reads a tsconfig that has comments, as TypeScript allows', () => {
    write('a/tsconfig.json', '{}')
    const root = write('root/tsconfig.json', '{\n  // the app\n  "references": [{ "path": "../a" }]\n}\n')

    expect(referencedProjects(root, ts)).toEqual([path.join(dir, 'a', 'tsconfig.json')])
  })

  it('refuses a root with no references: building it would check nothing', () => {
    const root = write('root/tsconfig.json', '{ "files": [] }')

    expect(() => referencedProjects(root, ts)).toThrow(/no "references"/)
  })

  it('refuses a root with an empty references list', () => {
    const root = write('root/tsconfig.json', '{ "files": [], "references": [] }')

    expect(() => referencedProjects(root, ts)).toThrow(/would check nothing/)
  })

  it("passes for this app's own root tsconfig, which references the app, vite config and e2e projects", () => {
    const frontendDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')

    const projects = referencedProjects(path.join(frontendDir, 'tsconfig.json'), ts).map(normalize)

    expect(projects.some((p) => p.endsWith('/tsconfig.app.json'))).toBe(true)
    expect(projects.some((p) => p.endsWith('/tsconfig.node.json'))).toBe(true)
    expect(projects.some((p) => p.endsWith('/tests/e2e/playwright/tsconfig.json'))).toBe(true)
  })
})

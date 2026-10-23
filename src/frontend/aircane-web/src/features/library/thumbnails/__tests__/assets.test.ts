// @vitest-environment node
import { mkdtemp, readdir, readFile, rm, stat, writeFile, mkdir } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import sharp from 'sharp'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { defaultPaths, run, TARGET_WIDTH } from '../../../../../scripts/build-thumbnails.mjs'
import { THUMBNAIL_MANIFEST } from '..'

const here = path.dirname(fileURLToPath(import.meta.url))
const webRoot = path.resolve(here, '../../../../..')
const repoRoot = path.resolve(webRoot, '../../..')

const quiet = { log: () => {}, warn: () => {} }

describe('committed thumbnail assets', () => {
  it('manifest matches the WebPs and metadata, without needing the PNG originals', async () => {
    const tmp = await mkdtemp(path.join(os.tmpdir(), 'thumb-manifest-'))
    try {
      const emptySource = path.join(tmp, 'no-originals')
      const regenerated = path.join(tmp, 'manifest.generated.ts')
      await run({ ...defaultPaths, sourceDir: emptySource, manifestFile: regenerated }, quiet)
      const expected = await readFile(regenerated, 'utf8')
      const committed = await readFile(defaultPaths.manifestFile, 'utf8')
      expect(committed.replace(/\r\n/g, '\n')).toBe(expected)
    } finally {
      await rm(tmp, { recursive: true, force: true })
    }
  })

  it('has a metadata entry for every WebP and a WebP for every entry', async () => {
    const webps = (await readdir(defaultPaths.outDir)).filter((f) => f.endsWith('.webp')).map((f) => f.replace('.webp', ''))
    const meta = JSON.parse(await readFile(defaultPaths.metaFile, 'utf8')) as Record<string, unknown>
    expect(Object.keys(meta).sort()).toEqual([...webps].sort())
    expect(THUMBNAIL_MANIFEST.map((e) => e.key).sort()).toEqual([...webps].sort())
    expect(THUMBNAIL_MANIFEST.some((e) => e.tags.includes('uncategorized'))).toBe(false)
  })

  it('contains exactly the 17 expected keys, each 320 px wide and about 3:4', () => {
    expect(THUMBNAIL_MANIFEST).toHaveLength(17)
    for (const e of THUMBNAIL_MANIFEST) {
      expect(e.width).toBe(TARGET_WIDTH)
      expect(Math.abs(e.width / e.height - 0.75)).toBeLessThan(0.75 * 0.04)
    }
  })

  it('no reference to a removed thumbnail file name remains', async () => {
    const oldNames =
      /document-thumbnail-(alien|callofcthulhu|daggerheart|gurps|lost-mine|pf2e|shadowrun|srd|startrek|starwars|vampirethemasquerade|warhammer-40k-[a-z]+|homebrew-monsters)|assets\/library\/document-thumbnail/
    const roots = [path.join(webRoot, 'src'), path.join(webRoot, 'index.html'), path.join(repoRoot, 'docs')]
    const offenders: string[] = []
    const visit = async (p: string): Promise<void> => {
      const s = await stat(p)
      if (s.isDirectory()) {
        for (const child of await readdir(p)) await visit(path.join(p, child))
      } else if (/\.(vue|ts|tsx|js|mjs|json|md|html|css)$/.test(p) && !p.endsWith('assets.test.ts')) {
        if (oldNames.test(await readFile(p, 'utf8'))) offenders.push(path.relative(repoRoot, p))
      }
    }
    for (const r of roots) if (existsSync(r)) await visit(r)
    expect(offenders).toEqual([])
  })
})

describe('thumbnails build script', () => {
  let tmp: string
  let paths: typeof defaultPaths

  const solid = (width: number, height: number) =>
    sharp({ create: { width, height, channels: 3, background: '#c8a050' } }).png().toBuffer()

  beforeEach(async () => {
    tmp = await mkdtemp(path.join(os.tmpdir(), 'thumb-build-'))
    paths = {
      sourceDir: path.join(tmp, 'src'),
      metaFile: path.join(tmp, 'src', 'thumbnails.meta.json'),
      outDir: path.join(tmp, 'out'),
      manifestFile: path.join(tmp, 'manifest.generated.ts'),
    }
    await mkdir(paths.sourceDir, { recursive: true })
    await writeFile(paths.metaFile, JSON.stringify({ tall: { label: 'Tall', tags: ['fantasy'] } }))
  })

  afterEach(() => rm(tmp, { recursive: true, force: true }))

  it('converts to 320 px WebP, never upscales, and flags non-3:4 sources without cropping', async () => {
    await writeFile(path.join(paths.sourceDir, 'tall.png'), await solid(900, 1200))
    await writeFile(path.join(paths.sourceDir, 'small.png'), await solid(120, 160))
    await writeFile(path.join(paths.sourceDir, 'square.png'), await solid(600, 600))
    const warnings: string[] = []
    const result = await run(paths, { log: () => {}, warn: (m: string) => warnings.push(m) })

    expect(result.conversion.converted.sort()).toEqual(['small', 'square', 'tall'])
    expect(result.conversion.flagged).toEqual(['square'])
    expect(warnings.some((w) => w.includes('square.png') && w.includes('3:4'))).toBe(true)
    expect(warnings.some((w) => w.includes('small.webp') && w.includes('uncategorized'))).toBe(true)

    const size = async (n: string) => sharp(path.join(paths.outDir, `${n}.webp`)).metadata()
    expect(await size('tall')).toMatchObject({ format: 'webp', width: 320, height: 427 })
    expect(await size('small')).toMatchObject({ width: 120, height: 160 })
    expect(await size('square')).toMatchObject({ width: 320, height: 320 })

    const manifest = await readFile(paths.manifestFile, 'utf8')
    expect(manifest).toContain("key: 'tall', file: 'tall.webp', label: 'Tall', tags: ['fantasy']")
    expect(manifest).toContain("key: 'small', file: 'small.webp', label: 'small', tags: ['uncategorized']")
  })

  it('is idempotent: a second run converts nothing and leaves every output untouched', async () => {
    await writeFile(path.join(paths.sourceDir, 'tall.png'), await solid(900, 1200))
    await run(paths, quiet)
    const out = path.join(paths.outDir, 'tall.webp')
    const manifestBefore = await readFile(paths.manifestFile, 'utf8')
    const webpBefore = await stat(out)

    const second = await run(paths, quiet)
    expect(second.conversion.converted).toEqual([])
    expect(second.conversion.skipped).toEqual(['tall'])
    expect(second.manifest.changed).toBe(false)
    expect((await stat(out)).mtimeMs).toBe(webpBefore.mtimeMs)
    expect(await readFile(paths.manifestFile, 'utf8')).toBe(manifestBefore)
  })

  it('with no PNG originals it converts nothing, says so, and still builds the manifest', async () => {
    await mkdir(paths.outDir, { recursive: true })
    await writeFile(path.join(paths.outDir, 'tall.webp'), await sharp(await solid(320, 427)).webp().toBuffer())
    const logs: string[] = []
    const result = await run(paths, { log: (m: string) => logs.push(m), warn: () => {} })
    expect(result.conversion.converted).toEqual([])
    expect(logs.some((l) => l.includes('No PNG originals'))).toBe(true)
    expect(await readFile(paths.manifestFile, 'utf8')).toContain("key: 'tall'")
  })
})

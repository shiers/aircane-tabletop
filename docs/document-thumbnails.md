# Document thumbnails

The library list shows a small 3:4 portrait next to each document. The artwork has a baked-in gold frame, so it is displayed at about 40 x 53 px with `object-fit: cover` and is not cropped to a square.

## Where things live

| What | Path | In git? |
| --- | --- | --- |
| Original PNGs (about 3 MB each) | `art-source/document-thumbnails/<key>.png` | **No** (ignored) |
| Metadata (label, tags) | `art-source/document-thumbnails/thumbnails.meta.json` | Yes |
| Optimised WebPs (320 px wide) | `src/frontend/aircane-web/public/assets/document-thumbnails/<key>.webp` | Yes |
| Generated manifest | `src/frontend/aircane-web/src/features/library/thumbnails/manifest.generated.ts` | Yes |
| Choice rules and URL helper | `src/frontend/aircane-web/src/features/library/thumbnails/index.ts` | Yes |

The **key** is the file stem, for example `fantasy-knight-pf`.

## Adding or replacing a thumbnail

1. Put the PNG in `art-source/document-thumbnails/`, named `<key>.png`. Aim for 3:4 portrait; the build warns if the ratio is more than about 4% off, and it never crops.
2. Add an entry to `thumbnails.meta.json`: `"<key>": { "label": "Readable name", "tags": ["fantasy"] }`. A WebP without an entry gets its stem as label, the tag `uncategorized`, and a build warning.
3. From `src/frontend/aircane-web` run:

   ```
   npm run thumbnails:build
   ```

   (`-- --force` re-converts everything.) It converts each PNG to a 320 px WebP at quality 80 (never upscaled), skips files that are already up to date, prints before and after sizes, and rewrites the manifest. It is plain Node (sharp), so it behaves the same in PowerShell, bash and CI.
4. Commit the WebP, `manifest.generated.ts` and `thumbnails.meta.json`. Do not edit the manifest by hand.
5. To have the new image chosen automatically, add a rule in `thumbnails/index.ts` (`SYSTEM_RULES`) and a test in `__tests__/resolve.test.ts`.

On a fresh clone there are no PNGs. `npm run thumbnails:build` then converts nothing, says so, and still rebuilds the manifest from the committed WebPs.

## Originals are not in git

The PNGs are about 3 MB each and every regenerated version would stay in repository history for good, so they are git-ignored. **Back them up yourself** (cloud drive, external disk); if the folder is lost, the WebPs in git are all that remains and cannot be re-exported at higher quality.

Alternative: track the originals with [Git LFS](https://git-lfs.com) (`git lfs track "art-source/document-thumbnails/*.png"`). That keeps history small and puts the originals under version control, at the cost of an LFS setup for everyone who clones. Not enabled today.

## How a thumbnail is chosen

`resolveThumbnailKey(doc, storedKey?)` returns a key; `thumbnailUrl(key)` turns it into a URL. A stored key (not wired up yet) always wins. Otherwise, in order, matching whole words, case-insensitively, on `gameSystem` and `ruleset` only (never the title):

1. Source type Homebrew: `homebrew`.
2. A named system: Pathfinder / pf2e, Call of Cthulhu / Cthulhu / CoC, Alien, Daggerheart, GURPS, Shadowrun, Vampire / Masquerade / VtM, Star Wars, Star Trek, Black Crusade, Rogue Trader, Dark Heresy, Deathwatch / Warhammer / 40k (see `SYSTEM_RULES` for the keys).
3. D&D, dnd, Dungeons, 5e, SRD: `cavern-adventure-lm` for Adventure and Solo, otherwise `fantasy-rulebook-dd`.
4. No match: Adventure and Solo use `cavern-adventure-lm`, Rules uses `fantasy-rulebook-dd`, everything else uses `generic`.

A key that is not in the manifest resolves to `generic`. A file that fails to load shows the existing `AircaneImg` fallback (`ThumbnailPlaceholder`).

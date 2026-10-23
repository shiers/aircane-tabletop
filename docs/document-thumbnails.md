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

`resolveThumbnailKey(doc, storedKey?)` returns a key; `thumbnailUrl(key)` turns it into a URL. A stored key (see below) wins when its image is still in the manifest. Otherwise, in order, matching whole words, case-insensitively, on `gameSystem` and `ruleset` only (never the title):

1. Source type Homebrew: `homebrew`.
2. A named system: Pathfinder / pf2e, Call of Cthulhu / Cthulhu / CoC, Alien, Daggerheart, GURPS, Shadowrun, Vampire / Masquerade / VtM, Star Wars, Star Trek, Black Crusade, Rogue Trader, Dark Heresy, Deathwatch / Warhammer / 40k (see `SYSTEM_RULES` for the keys).
3. D&D, dnd, Dungeons, 5e, SRD: `cavern-adventure-lm` for Adventure and Solo, otherwise `fantasy-rulebook-dd`.
4. No match: Adventure and Solo use `cavern-adventure-lm`, Rules uses `fantasy-rulebook-dd`, everything else uses `generic`.

`thumbnailUrl(key)` maps a key that is not in the manifest to `generic`. A file that fails to load shows the existing `AircaneImg` fallback (`ThumbnailPlaceholder`).

## Choosing a thumbnail per document (API)

`SourceDocument.ThumbnailKey` (nullable, max 64 characters, column `SourceDocuments.ThumbnailKey`) holds the host's choice. **Null means automatic**: the UI derives a thumbnail from the document's classification, as described above. Existing rows stay null. It is returned as `thumbnailKey` on every document DTO.

A key must match `^[a-z0-9]+(-[a-z0-9]+)*$` (at most 64 characters). An empty or whitespace-only value becomes null. Anything else is a 400 whose detail says "Thumbnail key must be 1-64 characters...". The backend cannot see the frontend manifest, so it checks the shape only, never that an image exists.

| Endpoint | Behaviour |
| --- | --- |
| `POST /api/library/documents` (multipart) | Optional form field `thumbnailKey`. One file per request, so the key goes with each file. |
| `PATCH /api/library/documents/{id}/thumbnail` | Body `{ "thumbnailKey": "fantasy-knight-pf" }`; `null` or `""` goes back to automatic. Host only (same `HostOnly` policy as the rest of the library). 404 for an unknown id, 400 for an invalid key, 200 with the updated document. Works on built-in documents (it is cosmetic). |
| `POST /api/library/folders/{id}/scan/import` | Optional `defaultThumbnailKey` (batch default) plus an optional `thumbnailKey` per item. A per-item key wins; an item without one gets the batch default; neither means automatic. |

The key is never reset by a folder scan, a re-scan that re-imports a changed file, a re-index, or built-in content seeding (the seeder skips documents it already created). A watched-folder default thumbnail (`DefaultThumbnailKey` on `WatchedFolder`) is not implemented.

## Where the stored key lives, and what null means

The host's choice is stored on the document as `thumbnailKey` (backend column `SourceDocuments.ThumbnailKey`, nullable, max 64). **`null` means automatic**: the rules above pick the thumbnail every time the list renders, so editing a document's system or ruleset changes its thumbnail too. A stored key pins it.

`resolveThumbnailKey(doc, doc.thumbnailKey)` is the single place that combines the two.

**When an asset is removed.** If a stored key no longer has an image in the manifest (for example you deleted a WebP), the key is ignored and the automatic choice is shown, not `generic`. The document keeps the stale key until someone changes it. The detail panel says "This thumbnail is no longer available", and the picker opens with that notice and Automatic selected. Choosing anything (or "Reset to automatic") replaces the stale key.

## Choosing a thumbnail in the UI

- **Library table:** click a row's thumbnail (it is a 44 px wide button) to open the picker for that document.
- **Detail panel:** select a row, then use "Change thumbnail" (a larger preview sits beside it) or "Reset to automatic" (only shown when a thumbnail was chosen).
- **Upload form:** once a file is chosen, the "Thumbnail" section shows the thumbnail the file will get. Its Automatic tile follows the source type, game system and ruleset typed so far.
- **Watched-folder review:** "Apply to all files in this import" sets a batch default; the thumbnail button on each selected file overrides it for that file.

The picker is a dialog with filter chips (All, Fantasy, Sci-fi, Horror, Grimdark, Generic, Homebrew, taken from the tags in `thumbnails.meta.json`), an "Automatic" tile that shows what would be picked right now, and the thumbnails. Arrow keys, Home and End move the selection, Escape closes, and focus stays inside the dialog. Changes in the library save with `PATCH`, update the row immediately, and roll back with the server's message if the request fails.

New components: `DocumentThumbnail` (draws a key inside a fixed-size wrapper; always give it a fixed size or `aspect-[3/4]`), `ThumbnailPicker`, `ThumbnailPickerDialog`, `ThumbnailField`.

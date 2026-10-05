# OCR for Scanned PDFs

Aircane Tabletop extracts text from PDFs so it can chunk, embed, and retrieve rules
content. Text-based PDFs work out of the box. **Scanned** PDFs (pages that are just
images with no embedded text layer) need Optical Character Recognition (OCR) to be
searchable.

OCR is **enabled by default** and is now a first-class capability. The desktop app
**bundles** everything OCR needs (the English language model and, on Windows, the native
Tesseract engine), so OCR works with **zero setup** there. OCR also drives the D&D Beyond
character-sheet PDF import, which rasterizes the sheet and reads values out of fixed
regions — see [Region OCR tuning](#region-ocr-tuning-dpi-and-psm) below.

OCR still **gates cleanly**: when it is turned off, or when the native engine / language
data is missing in a non-bundled environment, a scanned PDF is imported and marked
`OcrRequired` rather than failing — you can still see it in the library, it just won't be
searchable until OCR runs. A **shared/hosted deployment MAY set `Ocr:Enabled=false`**
(local-first OCR is not appropriate on a shared server).

## How it works

1. On import, the PDF text extractor reads the embedded text layer using PdfPig.
2. If a page has very little text (below ~50 characters/page on average) and OCR is
   available, the extractor pulls the page's embedded raster images and runs them
   through Tesseract.
3. Recognized text above the confidence threshold replaces the empty page text; the
   document is then chunked and embedded like any other.
4. If OCR is unavailable, or recognizes nothing usable, the document stays `OcrRequired`.

The OCR engine is loaded lazily and **gates cleanly**: if the native Tesseract library or
the language data is missing, the engine reports itself unavailable and the app keeps
working — it never crashes the import.

## Desktop app: bundled, zero setup

On the desktop app you do not install or download anything — the build fetches and bundles
the OCR assets next to the sidecar, and OCR is on by default. Two runtime pieces are
needed, and the desktop build handles both:

- **`eng.traineddata`** (the English model) — fetched and bundled on **every** target.
- **Native Tesseract engine** — on **Windows** this comes from the `TesseractOCR` NuGet
  payload via the self-contained sidecar publish, so no extra fetch is required (you still
  need the [Visual C++ 2022 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe), which
  the installer provides). On **Linux/macOS** the engine is system-provided unless the
  manifest pins a native bundle (see below) — those pins are mechanism-only for now, so if
  the system libs are absent OCR degrades gracefully rather than crashing.

### How the desktop build bundles OCR assets (pin-manifest / build-fetch flow)

Bundling mirrors the cloudflared sidecar precedent exactly:

1. The assets are **pinned** in `desktop/src-tauri/binaries/ocr-assets-versions.json`
   (tracked in git; gitignore-allowlisted). Each group (`tessdata`, optional `nativeLib`)
   carries its own top-level `version` + `baseUrl` and per-target `assets` with
   `asset`/`archive`/`sha256`.
2. During the build, `desktop/build.ps1` / `build.sh` (step 2c) compose the download URL as
   **three segments** — `baseUrl` + `/` + `version` + `/` + `asset` (`version` is a path
   element, never baked into `baseUrl`) — exactly like the cloudflared step.
3. The downloaded asset's SHA256 is verified against the pinned value **before** it is
   trusted or extracted. **The build FAILS on any checksum mismatch**, so an unverified
   binary is never bundled.
4. Verified assets are placed next to the sidecar (`binaries/tessdata/eng.traineddata`, and
   `binaries/ocr-native/` for native libs). The fetched binaries stay gitignored; only the
   manifest is tracked.

To update a pinned asset: re-download over HTTPS, recompute the SHA256, update
`ocr-assets-versions.json`, and record the change here. The current `tessdata` pin is
`eng.traineddata` from the Tesseract
[`tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast) release tagged in the
manifest. The Linux/macOS `nativeLib` SHA256 values are **documented placeholders** until
real pinned release assets land.

## Non-desktop setup (dev, Docker, servers, extra languages)

Outside the desktop bundle (local dev of the backend alone, Docker, a hosted server, or
adding a language beyond English) you provide the runtime pieces yourself. OCR is still
enabled by default; you only need to make the engine and tessdata reachable.

### 1. Install the native Tesseract runtime

- **Windows:** the `TesseractOCR` NuGet package bundles the native binaries. You only need
  the [Visual C++ 2022 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe).
- **Linux (incl. Docker):** install system Tesseract/Leptonica, e.g. on Debian/Ubuntu:
  `apt-get install -y libtesseract5 libleptonica-dev`.
- **macOS:** `brew install tesseract leptonica`.

### 2. Provide language data (tessdata)

Download the `*.traineddata` files for the languages you need from the Tesseract
[`tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast) repository (English is
`eng.traineddata`) and place them in a `tessdata` directory. By default the app looks for a
`tessdata` folder next to the backend binaries; point elsewhere with `Ocr:TessdataPath`. To
avoid the manual copy you can instead opt in to
[auto-downloading tessdata](#auto-downloading-tessdata-opt-in).

### 3. Configuration

OCR is on by default, so no change is needed to turn it on. The shipped `appsettings.json`
makes the defaults explicit:

```json
"Ocr": {
  "Enabled": true,
  "TessdataPath": "",
  "Language": "English",
  "MinConfidence": 0.3,
  "FullPageRasterization": true,
  "RasterizationDpi": 300,
  "RegionMinConfidence": 0.0
}
```

| Setting | Meaning |
|---------|---------|
| `Enabled` | Master switch, **default `true`**. When `false`, a no-op engine is used and scanned PDFs stay `OcrRequired`. A shared/hosted deployment MAY set this to `false`. |
| `TessdataPath` | Absolute or relative path to the `tessdata` directory. Empty = `tessdata` next to the app (the desktop bundle location). |
| `Language` | A `TesseractOCR` language name (e.g. `English`). Defaults to English (the only bundled model). |
| `MinConfidence` | 0..1. OCR **page**-level results below this mean confidence are discarded to avoid indexing garbage. Does not apply to the region path (see `RegionMinConfidence`). |
| `FullPageRasterization` | Default `true`. Renders low-text/vector pages to a bitmap and OCRs them (needed by the character-sheet path). See [Full-page rasterization](#full-page-rasterization-scanned-pdfs-without-embedded-images). |
| `RasterizationDpi` | Render resolution for full-page/region rasterization. Default `300`. A region tuning lever — see [Region OCR tuning](#region-ocr-tuning-dpi-and-psm). |
| `RegionMinConfidence` | 0..1. Confidence gate for the **region-anchored** path (character sheets). Default `0.0`: never drop at the engine level — a low-confidence region value is kept and flagged for review rather than silently discarded. |

Restart the backend after changing these values.

## Verifying

- Check the backend logs on startup / first import for an `OCR available:` or
  `OCR unavailable:` line describing the engine status.
- Re-import a scanned PDF. If OCR succeeded, its import status becomes `Completed` and its
  text becomes searchable; otherwise it remains `OcrRequired`.

## Licensing note

The OCR wrapper (`TesseractOCR`) and the Tesseract engine are Apache-2.0 licensed. Language
`traineddata` files carry their own licenses from the Tesseract project — review them if you
redistribute the app with bundled data.

## Full-page rasterization (scanned PDFs without embedded images)

Some scanned PDFs draw each page as vectors/curves rather than as an embedded raster image.
The default OCR path only sees embedded raster images, so it recovers nothing from these
pages. Full-page rasterization renders the whole page to a bitmap and OCRs that instead.

It is **on by default** (`FullPageRasterization: true`) because the character-sheet path
needs it, and it adds a native dependency (PDFium via the MIT-licensed
[`Docnet.Core`](https://github.com/GowenGit/docnet) package). The relevant settings:

```json
"Ocr": {
  "Enabled": true,
  "FullPageRasterization": true,
  "RasterizationDpi": 300
}
```

| Setting | Meaning |
|---------|---------|
| `FullPageRasterization` | When `true` (and `Enabled`), pages with little text AND no embedded raster images are rendered to a bitmap and OCR'd. Default `true`. |
| `RasterizationDpi` | Render resolution. Higher improves accuracy at the cost of memory/time. Default `300` (see [Region OCR tuning](#region-ocr-tuning-dpi-and-psm)). |

### PDFium native dependency

`Docnet.Core` ships the PDFium native binary for common runtimes (`win-x64`, `linux`,
`osx`). Docnet supports **x64 only**. If the native binary can't be loaded, the rasterizer
reports itself unavailable and the app logs a warning — full-page rasterization is skipped
and the rest of OCR (embedded-image path) still works. On AnyCPU builds you may need to set
the `DocnetRuntime` MSBuild property to force the correct native binary.

## Region OCR tuning (DPI and PSM)

The D&D Beyond character-sheet import does **region-anchored** OCR: it rasterizes a page
once, then crops the small rectangle around each value (ability scores, AC, HP, speed,
proficiency bonus, name, class & level, species/race, background) and OCRs each crop on its
own. Single-value crops like a two-digit ability score behave very differently from a full
page of prose, so recognition quality depends on **two independent levers** — do not assume
DPI alone governs it:

- **`RasterizationDpi` (resolution).** Default `300`. Character-sheet glyphs (e.g. the
  number inside an ability-score circle) are small, so they need more resolution than a
  bulk document scan. 300 is the manually-settled starting point; raise it (e.g. 400) if
  small-region reads come back empty or wrong, lower it if memory/time is a concern. This is
  a config-only change in `appsettings.json` — no code change.

- **Page-segmentation mode (PSM).** Tesseract defaults to PSM 3 (fully automatic page
  segmentation), which is tuned for a full page of text and recognizes **single tokens**
  (one number, one short word) poorly no matter how high the DPI is. A single-value crop
  generally wants a single-line / single-word / single-char mode (PSM 7/8/10). **DPI cannot
  fix a PSM problem and PSM cannot fix a DPI problem** — if a region reads blank or garbled
  at a known-good DPI, the segmentation mode is the next lever to check. Record the mode
  that works for each region class when tuning against a real sheet.

When adjusting these, change `RasterizationDpi` in `appsettings.json` and re-run the import;
the settled values for the D&D Beyond sheet are recorded during local tuning (DPI/PSM only,
no personal values). Because region recognition is validated end-to-end only against a local
PDF, the automated test suite exercises the pipeline (crop geometry, mapping, review
flagging) with a stub OCR engine rather than asserting real recognition accuracy.

## Auto-downloading tessdata (opt-in)

Instead of placing `eng.traineddata` manually, you can let the app fetch it on first run:

```json
"Ocr": {
  "Enabled": true,
  "AutoDownloadTessdata": true
}
```

When `AutoDownloadTessdata` is `true`, OCR is enabled, and `TessdataPath` is empty, the app
downloads `eng.traineddata` from the official Tesseract release to a local app-data
directory (`%LOCALAPPDATA%/Aircane/tessdata` on Windows, the platform equivalent elsewhere)
and points `TessdataPath` at it automatically. This is best-effort: if the download fails,
OCR simply stays unavailable and the logs explain that tessdata is missing. Leave it `false`
(the default) if your environment has no outbound internet or you prefer to manage tessdata
yourself.

## Re-running OCR after enabling it

Documents imported while OCR was off are marked `OcrRequired`. After you enable OCR you can
process them without re-importing:

- **Single document:** `POST /api/library/documents/{id}/reocr` (or the "Re-run OCR" button
  next to an OCR-required document in the library UI).
- **All at once:** `POST /api/library/documents/reocr-all`.

Both run as background jobs (they return `202 Accepted` with a job id) and re-enqueue the
import pipeline for each OCR-required document, which retries OCR now that it's available.

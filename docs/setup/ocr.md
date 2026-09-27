# OCR for Scanned PDFs

Aircane Tabletop extracts text from PDFs so it can chunk, embed, and retrieve rules
content. Text-based PDFs work out of the box. **Scanned** PDFs (pages that are just
images with no embedded text layer) need Optical Character Recognition (OCR) to be
searchable.

OCR is **optional and disabled by default** because it depends on a native library and
language data that are not bundled with the app. When OCR is disabled or unavailable, a
scanned PDF is imported and marked `OcrRequired` rather than failing — you can still see
it in the library, it just won't be searchable until OCR runs.

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

## Enabling OCR

### 1. Install the native Tesseract runtime

- **Windows:** the `TesseractOCR` NuGet package bundles the native binaries. You only need
  the [Visual C++ 2022 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe).
- **Linux (incl. Docker):** install system Tesseract/Leptonica, e.g. on Debian/Ubuntu:
  `apt-get install -y libtesseract5 libleptonica-dev`.
- **macOS:** `brew install tesseract leptonica`.

### 2. Provide language data (tessdata)

Download the `*.traineddata` files for the languages you need from the Tesseract
[`tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast) repository (English is
`eng.traineddata`) and place them in a `tessdata` directory.

By default the app looks for a `tessdata` folder next to the backend binaries. You can
point elsewhere with `Ocr:TessdataPath`.

### 3. Turn OCR on in configuration

In `appsettings.json` (or an environment override), set:

```json
"Ocr": {
  "Enabled": true,
  "TessdataPath": "",
  "Language": "English",
  "MinConfidence": 0.3
}
```

| Setting | Meaning |
|---------|---------|
| `Enabled` | Master switch. When `false`, a no-op engine is used and scanned PDFs stay `OcrRequired`. |
| `TessdataPath` | Absolute or relative path to the `tessdata` directory. Empty = `tessdata` next to the app. |
| `Language` | A `TesseractOCR` language name (e.g. `English`). Defaults to English. |
| `MinConfidence` | 0..1. OCR page results below this mean confidence are discarded to avoid indexing garbage. |

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

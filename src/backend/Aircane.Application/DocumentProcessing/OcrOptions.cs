namespace Aircane.Application.DocumentProcessing;

/// <summary>
/// Configuration for the optional OCR pipeline, bound from the <c>Ocr</c> config section.
/// </summary>
public sealed class OcrOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Ocr";

    /// <summary>
    /// Whether OCR is enabled at all. When <c>false</c>, a no-op engine is registered and
    /// scanned PDFs are always marked OCR-required. Defaults to <c>true</c>: OCR is now a
    /// first-class capability bundled with the desktop app. When enabled but the native engine
    /// or language data is missing the engine still gates cleanly to unavailable (never throws).
    /// A shared/hosted deployment MAY set this to <c>false</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the Tesseract <c>tessdata</c> directory containing <c>*.traineddata</c> files.
    /// When empty, a <c>tessdata</c> folder next to the application binaries is used.
    /// </summary>
    public string? TessdataPath { get; set; }

    /// <summary>
    /// TesseractOCR language enum name (e.g. <c>English</c>). Defaults to English.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Minimum mean confidence (0..1) an OCR <em>page</em> result must reach to be accepted.
    /// Results below this are discarded so garbage recognition does not pollute the index.
    /// This is the document page-level gate; the region-anchored path uses
    /// <see cref="RegionMinConfidence"/> instead.
    /// </summary>
    public float MinConfidence { get; set; } = 0.30f;

    /// <summary>
    /// Minimum confidence (0..1) applied by the region-anchored OCR path (<c>ICaptionRegionOcr</c>).
    /// Defaults to <c>0.0</c>: never drop at the engine level — a low-confidence region value is
    /// kept and the consumer flags it for review rather than silently discarding or guessing it.
    /// </summary>
    public float RegionMinConfidence { get; set; } = 0.0f;

    /// <summary>
    /// When <c>true</c> (and <see cref="Enabled"/> is true), pages that yield little text AND have
    /// no embedded raster images are rendered to a bitmap and OCR'd. Handles scanned PDFs whose
    /// pages are vector-drawn rather than embedded rasters. Requires a PDFium native dependency.
    /// Defaults to <c>true</c> so the character-sheet path works out of the box; the rasterizer gates
    /// cleanly to unavailable when PDFium is missing. See docs/setup/ocr.md.
    /// </summary>
    public bool FullPageRasterization { get; set; } = true;

    /// <summary>
    /// DPI used when rasterizing full pages for OCR. Higher improves OCR accuracy at the cost of
    /// memory/time. Defaults to 300: small character-sheet glyphs (ability-score circles) need
    /// more resolution than a bulk document scan.
    /// </summary>
    public int RasterizationDpi { get; set; } = 300;

    /// <summary>
    /// When <c>true</c> (and <see cref="Enabled"/> is true), if <see cref="TessdataPath"/> is not
    /// set or empty on startup, the app attempts to download <c>eng.traineddata</c> from the
    /// official Tesseract release into a local app-data directory and set the path automatically.
    /// Opt-in; off by default. See docs/setup/ocr.md.
    /// </summary>
    public bool AutoDownloadTessdata { get; set; }
}

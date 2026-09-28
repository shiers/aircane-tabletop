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
    /// scanned PDFs are always marked OCR-required. Defaults to <c>false</c> because OCR
    /// requires native binaries and language data that are not present by default.
    /// </summary>
    public bool Enabled { get; set; }

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
    /// Minimum mean confidence (0..1) an OCR page result must reach to be accepted.
    /// Results below this are discarded so garbage recognition does not pollute the index.
    /// </summary>
    public float MinConfidence { get; set; } = 0.30f;

    /// <summary>
    /// When <c>true</c> (and <see cref="Enabled"/> is true), pages that yield little text AND have
    /// no embedded raster images are rendered to a bitmap and OCR'd. Handles scanned PDFs whose
    /// pages are vector-drawn rather than embedded rasters. Requires a PDFium native dependency;
    /// off by default. See docs/setup/ocr.md.
    /// </summary>
    public bool FullPageRasterization { get; set; }

    /// <summary>
    /// DPI used when rasterizing full pages for OCR. Higher improves OCR accuracy at the cost of
    /// memory/time. Defaults to 200.
    /// </summary>
    public int RasterizationDpi { get; set; } = 200;

    /// <summary>
    /// When <c>true</c> (and <see cref="Enabled"/> is true), if <see cref="TessdataPath"/> is not
    /// set or empty on startup, the app attempts to download <c>eng.traineddata</c> from the
    /// official Tesseract release into a local app-data directory and set the path automatically.
    /// Opt-in; off by default. See docs/setup/ocr.md.
    /// </summary>
    public bool AutoDownloadTessdata { get; set; }
}

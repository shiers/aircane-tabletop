namespace Aircane.Application.Abstractions;

/// <summary>
/// Renders a PDF page to a raster image (PNG bytes) so it can be OCR'd. Used for scanned PDFs
/// whose pages are drawn as vectors/curves rather than as embedded raster images, which the
/// embedded-image OCR path cannot recover text from.
/// </summary>
/// <remarks>
/// Implementations wrap a native renderer (e.g. PDFium via Docnet.Core). The contract mirrors
/// <c>IOcrEngine</c>: never throw on unavailability — report it via <see cref="IsAvailable"/> and
/// return null from <see cref="RasterizePage"/> instead.
/// </remarks>
public interface IPdfRasterizer
{
    /// <summary>Whether the native rasterizer is available on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Renders the given 1-based page of the PDF (provided as raw bytes) to PNG image bytes at the
    /// configured DPI. Returns null if the page cannot be rendered or the rasterizer is unavailable.
    /// </summary>
    /// <param name="pdfBytes">The full PDF file bytes.</param>
    /// <param name="pageNumber">1-based page number to render.</param>
    /// <param name="ct">Cancellation token.</param>
    byte[]? RasterizePage(byte[] pdfBytes, int pageNumber, CancellationToken ct = default);
}

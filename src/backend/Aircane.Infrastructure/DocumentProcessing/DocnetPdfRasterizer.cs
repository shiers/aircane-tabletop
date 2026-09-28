using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// <see cref="IPdfRasterizer"/> backed by Docnet.Core (PDFium). Renders a PDF page to a 32-bit BMP
/// (which Leptonica/Tesseract can decode) at a DPI derived from <see cref="OcrOptions.RasterizationDpi"/>.
/// </summary>
/// <remarks>
/// Docnet returns a bottom-up BGRA pixel buffer; this class wraps it in a BMP header rather than
/// pulling in a full image library. Availability is probed lazily: if the native PDFium binary is
/// missing, <see cref="IsAvailable"/> reports false and <see cref="RasterizePage"/> returns null.
/// </remarks>
public sealed class DocnetPdfRasterizer : IPdfRasterizer
{
    private readonly int _dpi;
    private readonly ILogger<DocnetPdfRasterizer> _logger;
    private readonly bool _available;

    public DocnetPdfRasterizer(OcrOptions options, ILogger<DocnetPdfRasterizer> logger)
    {
        _logger = logger;
        _dpi = options.RasterizationDpi > 0 ? options.RasterizationDpi : 200;

        // Probe native availability once. DocLib.Instance touches the native binary.
        try
        {
            _ = DocLib.Instance;
            _available = true;
        }
        catch (Exception ex)
        {
            _available = false;
            _logger.LogWarning(ex,
                "PDF rasterizer unavailable: the PDFium native library could not be loaded. " +
                "Full-page rasterization OCR will be skipped.");
        }
    }

    /// <inheritdoc />
    public bool IsAvailable => _available;

    /// <inheritdoc />
    public byte[]? RasterizePage(byte[] pdfBytes, int pageNumber, CancellationToken ct = default)
    {
        if (!_available || pdfBytes.Length == 0 || pageNumber < 1)
            return null;

        try
        {
            ct.ThrowIfCancellationRequested();

            // Render at a scale derived from DPI (PDF user space is 72 DPI).
            var scale = _dpi / 72.0;
            using var docReader = DocLib.Instance.GetDocReader(
                pdfBytes,
                new PageDimensions(scale));

            using var pageReader = docReader.GetPageReader(pageNumber - 1);
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();
            if (width <= 0 || height <= 0)
                return null;

            var bgra = pageReader.GetImage(); // 4 bytes/pixel BGRA, top-down
            if (bgra.Length < width * height * 4)
                return null;

            return BuildBmp(bgra, width, height);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rasterize PDF page {PageNumber}.", pageNumber);
            return null;
        }
    }

    /// <summary>
    /// Wraps a top-down BGRA pixel buffer in a 32-bit BMP. BMP scanlines are stored bottom-up, so
    /// rows are emitted in reverse. 32-bit BMP rows are already 4-byte aligned (no padding needed).
    /// </summary>
    private static byte[] BuildBmp(byte[] bgraTopDown, int width, int height)
    {
        const int headerSize = 54; // 14-byte file header + 40-byte BITMAPINFOHEADER
        var pixelDataSize = width * height * 4;
        var fileSize = headerSize + pixelDataSize;

        var bmp = new byte[fileSize];

        // BITMAPFILEHEADER
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt32(bmp, 10, headerSize); // pixel data offset

        // BITMAPINFOHEADER
        WriteInt32(bmp, 14, 40);          // header size
        WriteInt32(bmp, 18, width);
        WriteInt32(bmp, 22, height);      // positive => bottom-up
        bmp[26] = 1; bmp[27] = 0;         // planes
        bmp[28] = 32; bmp[29] = 0;        // bits per pixel
        WriteInt32(bmp, 34, pixelDataSize);

        // Copy rows in reverse (BMP is bottom-up; source is top-down).
        var rowBytes = width * 4;
        for (var srcRow = 0; srcRow < height; srcRow++)
        {
            var dstRow = height - 1 - srcRow;
            Array.Copy(
                bgraTopDown, srcRow * rowBytes,
                bmp, headerSize + dstRow * rowBytes,
                rowBytes);
        }

        return bmp;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

/// <summary>
/// No-op <see cref="IPdfRasterizer"/> used when full-page rasterization is disabled. Always
/// unavailable; <see cref="RasterizePage"/> returns null.
/// </summary>
public sealed class NullPdfRasterizer : IPdfRasterizer
{
    public bool IsAvailable => false;
    public byte[]? RasterizePage(byte[] pdfBytes, int pageNumber, CancellationToken ct = default) => null;
}

using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Microsoft.Extensions.Logging;

namespace Aircane.Infrastructure.DocumentProcessing;

/// <summary>
/// <see cref="ICaptionRegionOcr"/> backed by the existing <see cref="IPdfRasterizer"/> +
/// <see cref="IOcrEngine"/> seam. Rasterizes a page once, then crops and OCRs each named region.
/// </summary>
/// <remarks>
/// <para>
/// Cropping is pure byte math over the 32bpp BMP that <see cref="DocnetPdfRasterizer.BuildBmp"/>
/// already produces (no image library). That BMP is written with a POSITIVE height
/// (<c>BITMAPINFOHEADER</c> biHeight &gt; 0), which means it is stored <em>bottom-up</em>: BMP row 0
/// is the BOTTOM of the page. <see cref="OcrRegion"/> coordinates are TOP-DOWN (Y from the top), so
/// the crop inverts: a top-down source row <c>srcRowFromTop</c> maps to BMP row
/// <c>(bmpHeight - 1) - srcRowFromTop</c>.
/// </para>
/// <para>
/// Honours the never-throw contract (NFR-1): any out-of-bounds, failed, or empty region yields
/// <c>(key, "", 0f)</c>; a null rasterization yields an empty list.
/// </para>
/// </remarks>
public sealed class CaptionRegionOcr : ICaptionRegionOcr
{
    private const int HeaderSize = 54; // 14-byte file header + 40-byte BITMAPINFOHEADER
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Region keys (caption tokens) whose values are a single numeric token. These OCR best under
    /// single-word segmentation (PSM 8). Every other region key may hold a multi-word value (name,
    /// class &amp; level, species/race, background) and uses single-line segmentation (PSM 7).
    /// Matched case-insensitively on the normalized caption token.
    /// </summary>
    private static readonly IReadOnlySet<string> SingleWordRegionKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "STRENGTH",
            "DEXTERITY",
            "CONSTITUTION",
            "INTELLIGENCE",
            "WISDOM",
            "CHARISMA",
            "ARMOR",
            "ARMOR CLASS",
            "HIT POINTS",
            "SPEED",
            "PROFICIENCY BONUS",
        };

    private readonly IPdfRasterizer _rasterizer;
    private readonly IOcrEngine _ocrEngine;
    private readonly OcrOptions _ocrOptions;
    private readonly ILogger<CaptionRegionOcr> _logger;

    public CaptionRegionOcr(
        IPdfRasterizer rasterizer,
        IOcrEngine ocrEngine,
        OcrOptions ocrOptions,
        ILogger<CaptionRegionOcr> logger)
    {
        _rasterizer = rasterizer;
        _ocrEngine = ocrEngine;
        _ocrOptions = ocrOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _rasterizer.IsAvailable && _ocrEngine.IsAvailable;

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegionOcrResult>> RecognizeRegionsAsync(
        byte[] pdfBytes, int pageNumber, IReadOnlyCollection<OcrRegion> regions, CancellationToken ct = default)
    {
        if (regions is null || regions.Count == 0)
            return Array.Empty<RegionOcrResult>();

        if (!IsAvailable)
            return Array.Empty<RegionOcrResult>();

        byte[]? pageBmp;
        try
        {
            pageBmp = _rasterizer.RasterizePage(pdfBytes, pageNumber, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Region OCR: rasterizing page {PageNumber} failed; returning empty.", pageNumber);
            return Array.Empty<RegionOcrResult>();
        }

        if (pageBmp is null || pageBmp.Length < HeaderSize)
        {
            _logger.LogWarning("Region OCR: page {PageNumber} did not rasterize; returning empty.", pageNumber);
            return Array.Empty<RegionOcrResult>();
        }

        if (!TryReadBmpDimensions(pageBmp, out var bmpWidth, out var bmpHeight))
        {
            _logger.LogWarning("Region OCR: rasterized page {PageNumber} is not a 32bpp bottom-up BMP; returning empty.",
                pageNumber);
            return Array.Empty<RegionOcrResult>();
        }

        var results = new List<RegionOcrResult>(regions.Count);
        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();

            var crop = CropRegion(pageBmp, bmpWidth, bmpHeight, region);
            if (crop is null)
            {
                _logger.LogDebug("Region OCR: region '{Key}' is out of bounds; emitting empty.", region.Key);
                results.Add(new RegionOcrResult(region.Key, string.Empty, 0f));
                continue;
            }

            OcrResult ocr;
            try
            {
                ocr = await _ocrEngine.RecognizeAsync(
                    crop, _ocrOptions.RegionMinConfidence, SegmentationFor(region.Key), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Region OCR: recognizing region '{Key}' failed; emitting empty.", region.Key);
                results.Add(new RegionOcrResult(region.Key, string.Empty, 0f));
                continue;
            }

            results.Add(new RegionOcrResult(region.Key, ocr.Text ?? string.Empty, ocr.MeanConfidence));
        }

        return results;
    }

    /// <summary>
    /// Confirms the buffer is a 32bpp, positive-height (bottom-up) BMP and reads its pixel
    /// dimensions. Returns false for any other layout.
    /// </summary>
    private static bool TryReadBmpDimensions(byte[] bmp, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bmp.Length < HeaderSize || bmp[0] != (byte)'B' || bmp[1] != (byte)'M')
            return false;

        var w = ReadInt32(bmp, 18);
        var h = ReadInt32(bmp, 22);
        var bpp = bmp[28] | (bmp[29] << 8);

        // Only the bottom-up (positive height) 32bpp BMP the rasterizer emits is supported.
        if (w <= 0 || h <= 0 || bpp != 32)
            return false;

        if (bmp.Length < HeaderSize + (long)w * h * BytesPerPixel)
            return false;

        width = w;
        height = h;
        return true;
    }

    /// <summary>
    /// Crops <paramref name="region"/> (normalized, top-down coords) out of a bottom-up 32bpp BMP
    /// into a new standalone bottom-up 32bpp BMP. Returns null when the region maps to a zero-area
    /// or out-of-bounds rectangle.
    /// </summary>
    private static byte[]? CropRegion(byte[] src, int bmpWidth, int bmpHeight, OcrRegion region)
    {
        // Map normalized coords to a top-down pixel rectangle, clamped to the page.
        var px = (int)Math.Floor(Clamp01(region.X) * bmpWidth);
        var pyTop = (int)Math.Floor(Clamp01(region.Y) * bmpHeight);
        var pw = (int)Math.Round(Clamp01(region.Width) * bmpWidth);
        var ph = (int)Math.Round(Clamp01(region.Height) * bmpHeight);

        if (px < 0) px = 0;
        if (pyTop < 0) pyTop = 0;
        if (px >= bmpWidth || pyTop >= bmpHeight)
            return null;

        if (px + pw > bmpWidth) pw = bmpWidth - px;
        if (pyTop + ph > bmpHeight) ph = bmpHeight - pyTop;

        if (pw <= 0 || ph <= 0)
            return null;

        var srcRowBytes = bmpWidth * BytesPerPixel;
        var dstRowBytes = pw * BytesPerPixel;
        var pixelDataSize = dstRowBytes * ph;
        var fileSize = HeaderSize + pixelDataSize;

        var dst = new byte[fileSize];

        // BITMAPFILEHEADER
        dst[0] = (byte)'B';
        dst[1] = (byte)'M';
        WriteInt32(dst, 2, fileSize);
        WriteInt32(dst, 10, HeaderSize); // pixel data offset

        // BITMAPINFOHEADER (positive height => bottom-up, matching the source layout)
        WriteInt32(dst, 14, 40);
        WriteInt32(dst, 18, pw);
        WriteInt32(dst, 22, ph);
        dst[26] = 1; dst[27] = 0;   // planes
        dst[28] = 32; dst[29] = 0;  // bits per pixel
        WriteInt32(dst, 34, pixelDataSize);

        // The destination is also bottom-up. We copy region rows top-down (row 0 = top of the crop),
        // but place each into the destination bottom-up, and read from the source bottom-up with the
        // inversion (bmpHeight - 1 - srcRowFromTop) so the geometry is preserved end to end.
        var colOffsetBytes = px * BytesPerPixel;
        for (var cropRow = 0; cropRow < ph; cropRow++)
        {
            var srcRowFromTop = pyTop + cropRow;
            var srcBmpRow = (bmpHeight - 1) - srcRowFromTop; // invert: BMP row 0 is the page BOTTOM
            var dstBmpRow = (ph - 1) - cropRow;              // keep the crop bottom-up as well

            var srcStart = HeaderSize + srcBmpRow * srcRowBytes + colOffsetBytes;
            var dstStart = HeaderSize + dstBmpRow * dstRowBytes;
            Array.Copy(src, srcStart, dst, dstStart, dstRowBytes);
        }

        return dst;
    }

    /// <summary>
    /// Chooses the page-segmentation hint for a value region. Single-token numeric fields (abilities,
    /// AC, HP, speed, proficiency bonus) use <see cref="OcrSegmentationMode.SingleWord"/>; every other
    /// region (potentially multi-word text values) defaults to <see cref="OcrSegmentationMode.SingleLine"/>.
    /// Both recover text that full-page auto segmentation reads as empty on tight single-value crops.
    /// </summary>
    private static OcrSegmentationMode SegmentationFor(string regionKey)
        => SingleWordRegionKeys.Contains(regionKey)
            ? OcrSegmentationMode.SingleWord
            : OcrSegmentationMode.SingleLine;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static int ReadInt32(byte[] buffer, int offset)
        => buffer[offset]
           | (buffer[offset + 1] << 8)
           | (buffer[offset + 2] << 16)
           | (buffer[offset + 3] << 24);

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

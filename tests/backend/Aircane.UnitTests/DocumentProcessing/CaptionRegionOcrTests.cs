using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Aircane.Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Tests the generic region-anchored OCR helper. A stub rasterizer returns a hand-built 32bpp
/// bottom-up BMP whose top and bottom halves are painted with distinct colours; a stub OCR engine
/// identifies which half a crop came from by sampling a pixel and returns a known label. This proves
/// the crop lands on the correct rectangle AND that the bottom-up BMP inversion is correct (a region
/// anchored to the TOP of the page returns the TOP label, not the mirror band).
/// </summary>
public class CaptionRegionOcrTests
{
    private const int BmpWidth = 40;
    private const int BmpHeight = 40;

    // Distinct per-band blue-channel markers (BGRA).
    private const byte TopBlue = 0x11;
    private const byte BottomBlue = 0xEE;

    [Fact]
    public async Task RecognizeRegions_TopAnchoredRegion_ReturnsTopPattern_ProvingBottomUpInversion()
    {
        var sut = CreateSut(out _, out _);

        var regions = new[]
        {
            // Top band: Y from 0.0 (page top), height 0.4.
            new OcrRegion("top", X: 0.0, Y: 0.0, Width: 1.0, Height: 0.4),
            // Bottom band: Y from 0.6 (lower on the page), height 0.4.
            new OcrRegion("bottom", X: 0.0, Y: 0.6, Width: 1.0, Height: 0.4),
        };

        var results = await sut.RecognizeRegionsAsync(new byte[] { 1 }, 1, regions);

        Assert.Equal(2, results.Count);
        Assert.Equal("TOP", results.Single(r => r.Key == "top").Text);
        Assert.Equal("BOTTOM", results.Single(r => r.Key == "bottom").Text);
    }

    [Fact]
    public async Task RecognizeRegions_OutOfBoundsRegion_ReturnsEmptyZeroConfidence()
    {
        var sut = CreateSut(out _, out _);

        var regions = new[]
        {
            new OcrRegion("off", X: 1.5, Y: 1.5, Width: 0.2, Height: 0.2),
        };

        var results = await sut.RecognizeRegionsAsync(new byte[] { 1 }, 1, regions);

        var only = Assert.Single(results);
        Assert.Equal("off", only.Key);
        Assert.Equal(string.Empty, only.Text);
        Assert.Equal(0f, only.Confidence);
    }

    [Fact]
    public async Task RecognizeRegions_WhenEngineUnavailable_ReturnsEmptyList_AndIsNotAvailable()
    {
        var rasterizer = new StubRasterizer(BuildSyntheticBmp()) { Available = true };
        var engine = new MarkerOcrEngine { Available = false };
        var sut = new CaptionRegionOcr(rasterizer, engine, new OcrOptions(), NullLogger<CaptionRegionOcr>.Instance);

        Assert.False(sut.IsAvailable);

        var results = await sut.RecognizeRegionsAsync(
            new byte[] { 1 }, 1, new[] { new OcrRegion("top", 0, 0, 1, 0.4) });

        Assert.Empty(results);
    }

    [Fact]
    public async Task RecognizeRegions_WhenRasterizerReturnsNull_ReturnsEmptyList()
    {
        var rasterizer = new StubRasterizer(null) { Available = true };
        var engine = new MarkerOcrEngine { Available = true };
        var sut = new CaptionRegionOcr(rasterizer, engine, new OcrOptions(), NullLogger<CaptionRegionOcr>.Instance);

        Assert.True(sut.IsAvailable);

        var results = await sut.RecognizeRegionsAsync(
            new byte[] { 1 }, 1, new[] { new OcrRegion("top", 0, 0, 1, 0.4) });

        Assert.Empty(results);
    }

    [Fact]
    public async Task RecognizeRegions_PassesRegionMinConfidence_NotPageGate()
    {
        var sut = CreateSut(out _, out var engine);

        await sut.RecognizeRegionsAsync(
            new byte[] { 1 }, 1, new[] { new OcrRegion("top", 0, 0, 1, 0.4) });

        // RegionMinConfidence default is 0.0 — the region path must not pass the 0.30 page gate.
        Assert.Equal(0f, engine.LastMinConfidenceOverride);
    }

    [Fact]
    public async Task RecognizeRegions_RequestsPerRegionSegmentationMode_NumericSingleWord_TextSingleLine()
    {
        var sut = CreateSut(out _, out var engine);

        // Order matters: the region path OCRs regions in iteration order, so Calls[i] ↔ regions[i].
        var regions = new[]
        {
            new OcrRegion("STRENGTH", X: 0.0, Y: 0.0, Width: 1.0, Height: 0.4),       // numeric single-token
            new OcrRegion("CHARACTER NAME", X: 0.0, Y: 0.0, Width: 1.0, Height: 0.4), // potentially multi-word
        };

        await sut.RecognizeRegionsAsync(new byte[] { 1 }, 1, regions);

        Assert.Equal(2, engine.Calls.Count);
        Assert.Equal(OcrSegmentationMode.SingleWord, engine.Calls[0].Mode);
        Assert.Equal(OcrSegmentationMode.SingleLine, engine.Calls[1].Mode);

        // The value regions must NEVER fall back to full-page auto (the bug that read nothing).
        Assert.All(engine.Calls, c => Assert.NotEqual(OcrSegmentationMode.Default, c.Mode));
    }

    // ── Helpers / test doubles ────────────────────────────────────────────────

    private static CaptionRegionOcr CreateSut(out StubRasterizer rasterizer, out MarkerOcrEngine engine)
    {
        rasterizer = new StubRasterizer(BuildSyntheticBmp()) { Available = true };
        engine = new MarkerOcrEngine { Available = true };
        return new CaptionRegionOcr(rasterizer, engine, new OcrOptions(), NullLogger<CaptionRegionOcr>.Instance);
    }

    /// <summary>
    /// Builds a 32bpp bottom-up BMP matching DocnetPdfRasterizer.BuildBmp's layout: the TOP half of
    /// the page (small top-down Y) is painted with <see cref="TopBlue"/>, the bottom half with
    /// <see cref="BottomBlue"/>. We author in top-down terms then emit rows reversed (bottom-up).
    /// </summary>
    private static byte[] BuildSyntheticBmp()
    {
        const int headerSize = 54;
        const int bpp = 4;
        var rowBytes = BmpWidth * bpp;
        var pixelDataSize = rowBytes * BmpHeight;
        var fileSize = headerSize + pixelDataSize;

        var bmp = new byte[fileSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteInt32(bmp, 2, fileSize);
        WriteInt32(bmp, 10, headerSize);
        WriteInt32(bmp, 14, 40);
        WriteInt32(bmp, 18, BmpWidth);
        WriteInt32(bmp, 22, BmpHeight); // positive => bottom-up
        bmp[26] = 1;
        bmp[28] = 32;
        WriteInt32(bmp, 34, pixelDataSize);

        // Author in top-down logical rows, then write to the bottom-up BMP (row 0 = page bottom).
        for (var srcRowFromTop = 0; srcRowFromTop < BmpHeight; srcRowFromTop++)
        {
            var blue = srcRowFromTop < BmpHeight / 2 ? TopBlue : BottomBlue;
            var bmpRow = (BmpHeight - 1) - srcRowFromTop;
            var rowStart = headerSize + bmpRow * rowBytes;
            for (var x = 0; x < BmpWidth; x++)
            {
                var p = rowStart + x * bpp;
                bmp[p] = blue;          // B
                bmp[p + 1] = 0x00;      // G
                bmp[p + 2] = 0x00;      // R
                bmp[p + 3] = 0xFF;      // A
            }
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

    private sealed class StubRasterizer : IPdfRasterizer
    {
        private readonly byte[]? _bmp;
        public StubRasterizer(byte[]? bmp) => _bmp = bmp;
        public bool Available { get; init; }
        public bool IsAvailable => Available;
        public byte[]? RasterizePage(byte[] pdfBytes, int pageNumber, CancellationToken ct = default) => _bmp;
    }

    /// <summary>
    /// Reads the blue channel of the first pixel of the received crop and maps it to a known label,
    /// so an assertion can prove the crop came from the expected band.
    /// </summary>
    private sealed class MarkerOcrEngine : IOcrEngine
    {
        public bool Available { get; init; }
        public bool IsAvailable => Available;
        public string StatusDescription => "Marker OCR engine (test).";
        public float? LastMinConfidenceOverride { get; private set; }
        public OcrSegmentationMode LastSegmentationMode { get; private set; }
        public List<(string? Text, OcrSegmentationMode Mode)> Calls { get; } = new();

        public Task<OcrResult> RecognizeAsync(
            byte[] imageBytes,
            float? minConfidenceOverride = null,
            OcrSegmentationMode segmentationMode = OcrSegmentationMode.Default,
            CancellationToken ct = default)
        {
            LastMinConfidenceOverride = minConfidenceOverride;
            LastSegmentationMode = segmentationMode;
            Calls.Add((null, segmentationMode));

            // First pixel of a 32bpp bottom-up BMP: header (54) + B channel.
            if (imageBytes.Length <= 54)
                return Task.FromResult(OcrResult.Empty);

            var blue = imageBytes[54];
            var label = blue switch
            {
                TopBlue => "TOP",
                BottomBlue => "BOTTOM",
                _ => string.Empty,
            };
            return Task.FromResult(new OcrResult(label, 0.5f));
        }
    }
}

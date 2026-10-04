using Aircane.Application.DocumentProcessing;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Pins the enabled-by-default OCR CLR defaults (acc.1). OCR is now a first-class capability, so a
/// freshly constructed <see cref="OcrOptions"/> must have OCR and full-page rasterization on, a
/// 300-DPI raster default, and a 0.0 region confidence floor (never drop at the engine level).
/// </summary>
public class OcrOptionsDefaultsTests
{
    [Fact]
    public void NewOcrOptions_HasFirstClassDefaults()
    {
        var options = new OcrOptions();

        Assert.True(options.Enabled);
        Assert.True(options.FullPageRasterization);
        Assert.Equal(300, options.RasterizationDpi);
        Assert.Equal(0f, options.RegionMinConfidence);
    }

    [Fact]
    public void NewOcrOptions_LeavesDocumentPageGateUnchanged()
    {
        // The document page-level gate must stay at 0.30 — only the region path uses 0.0.
        Assert.Equal(0.30f, new OcrOptions().MinConfidence);
    }
}

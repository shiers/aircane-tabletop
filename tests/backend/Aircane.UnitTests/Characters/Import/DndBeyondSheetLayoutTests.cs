using Aircane.Application.Abstractions;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

/// <summary>
/// Tests for the DDB caption → value-region geometry. All geometry is synthetic. Verifies the
/// offset table math, [0,1] clamping, and the absolute-region fallback for an absent caption.
/// </summary>
public sealed class DndBeyondSheetLayoutTests
{
    private const double Tol = 1e-9;

    [Fact]
    public void RegionFromCaptionBox_AppliesOffsetTableMath()
    {
        // Synthetic caption box well inside the page so no clamping occurs.
        var box = new CaptionBox(X: 0.10, Y: 0.20, Width: 0.08, Height: 0.03);
        var offset = new DndBeyondSheetLayout.Offset(Dx: 0.0, Dy: 1.1, Wx: 1.0, Wy: 1.6);

        var region = DndBeyondSheetLayout.RegionFromCaptionBox("STRENGTH", box, offset);

        // x = cx + dx*cw = 0.10; y = cy + dy*ch = 0.20 + 1.1*0.03 = 0.233
        // w = wx*cw = 0.08; h = wy*ch = 1.6*0.03 = 0.048
        Assert.Equal("STRENGTH", region.Key);
        Assert.Equal(0.10, region.X, Tol);
        Assert.Equal(0.233, region.Y, Tol);
        Assert.Equal(0.08, region.Width, Tol);
        Assert.Equal(0.048, region.Height, Tol);
    }

    [Fact]
    public void RegionFromCaptionBox_ClampsToUnitSquare()
    {
        // Caption box near the right/bottom edge with a wide offset → region clamps to [0,1].
        var box = new CaptionBox(X: 0.95, Y: 0.97, Width: 0.1, Height: 0.1);
        var offset = new DndBeyondSheetLayout.Offset(Dx: 0.0, Dy: 1.0, Wx: 3.0, Wy: 2.0);

        var region = DndBeyondSheetLayout.RegionFromCaptionBox("CHARACTER NAME", box, offset);

        Assert.InRange(region.X, 0.0, 1.0);
        Assert.InRange(region.Y, 0.0, 1.0);
        Assert.InRange(region.X + region.Width, 0.0, 1.0 + Tol);
        Assert.InRange(region.Y + region.Height, 0.0, 1.0 + Tol);
    }

    [Fact]
    public void BuildRegions_InScopeCaptions_ProduceRegionsMatchingOffsetTable()
    {
        var boxes = new Dictionary<string, CaptionBox>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["STRENGTH"] = new(0.03, 0.26, 0.08, 0.02),
            ["ARMOR"] = new(0.18, 0.20, 0.06, 0.02),
            ["HIT POINTS"] = new(0.30, 0.20, 0.08, 0.02),
        };

        var regions = DndBeyondSheetLayout.BuildRegions(boxes);

        // Each supplied caption yields exactly one region matching the per-caption offset.
        var str = Assert.Single(regions, r => r.Key == "STRENGTH");
        var expectedStr = DndBeyondSheetLayout.RegionFromCaptionBox(
            "STRENGTH", boxes["STRENGTH"], DndBeyondSheetLayout.Offsets["STRENGTH"]);
        Assert.Equal(expectedStr.Y, str.Y, Tol);
        Assert.Equal(expectedStr.Height, str.Height, Tol);

        Assert.Contains(regions, r => r.Key == "ARMOR");
        Assert.Contains(regions, r => r.Key == "HIT POINTS");
    }

    [Fact]
    public void BuildRegions_AbsentCaption_UsesAbsoluteFallback()
    {
        // No caption boxes supplied → every in-scope anchor with a fallback uses the absolute region.
        var regions = DndBeyondSheetLayout.BuildRegions(
            new Dictionary<string, CaptionBox>(System.StringComparer.OrdinalIgnoreCase));

        var strength = Assert.Single(regions, r => r.Key == "STRENGTH");
        Assert.Equal(DndBeyondSheetLayout.AbsoluteFallbacks["STRENGTH"], strength);
    }

    [Fact]
    public void BuildRegions_AllRegionsAreWithinUnitSquare()
    {
        var regions = DndBeyondSheetLayout.BuildRegions(
            new Dictionary<string, CaptionBox>(System.StringComparer.OrdinalIgnoreCase));

        foreach (var r in regions)
        {
            Assert.InRange(r.X, 0.0, 1.0);
            Assert.InRange(r.Y, 0.0, 1.0);
            Assert.InRange(r.X + r.Width, 0.0, 1.0 + Tol);
            Assert.InRange(r.Y + r.Height, 0.0, 1.0 + Tol);
        }
    }

    [Fact]
    public void Offsets_AnchorKeys_MatchCaptionMapVocabulary()
    {
        // Anchor keys that map to a canonical value must exist in CaptionMap (shared vocabulary);
        // CLASS & LEVEL is the one anchor handled via the class/level split, not CaptionMap.
        foreach (var key in DndBeyondSheetLayout.Offsets.Keys)
        {
            if (string.Equals(key, DndBeyondPdfHints.ClassLevelCaption, System.StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.True(DndBeyondPdfHints.CaptionMap.ContainsKey(key), $"'{key}' should be in CaptionMap.");
        }
    }
}

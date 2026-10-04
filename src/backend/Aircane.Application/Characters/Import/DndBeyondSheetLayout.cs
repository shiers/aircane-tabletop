using Aircane.Application.Abstractions;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// A caption's bounding box in normalized, TOP-DOWN page coordinates (0..1 of page width/height;
/// Y measured from the TOP of the page). Produced by the extractor from PdfPig word geometry.
/// </summary>
/// <param name="X">Left edge, 0..1 of page width.</param>
/// <param name="Y">Top edge, 0..1 of page height (from the page TOP).</param>
/// <param name="Width">Width, 0..1 of page width.</param>
/// <param name="Height">Height, 0..1 of page height.</param>
public readonly record struct CaptionBox(double X, double Y, double Width, double Height);

/// <summary>
/// Translates DDB printable-sheet caption geometry into value <see cref="OcrRegion"/>s. For a
/// caption box <c>(cx, cy, cw, ch)</c> (top-down) the value region is
/// <c>{ x = cx + dx·cw, y = cy + dy·ch, w = wx·cw, h = wy·ch }</c>, already normalized to [0,1]
/// (the caption box is normalized), then clamped to the page.
/// <para>
/// Anchor keys are the SAME verified caption tokens as <see cref="DndBeyondPdfHints.CaptionMap"/>
/// and the signature detector (one vocabulary): e.g. <c>ARMOR</c> for AC, <c>HIT POINTS</c> for the
/// max-HP box (the extractor disambiguates the HP captions by position before handing boxes here).
/// </para>
/// <para>
/// When a caption is absent from the text layer, a <see cref="AbsoluteFallbacks">normalized-absolute
/// region</see> is used instead. An anchor that resolves to neither yields no region — the value is
/// flagged for review downstream, never guessed (FR-4.3).
/// </para>
/// Offsets are derived from the public DDB template layout (caption-above-value boxes) and are
/// tuned manually against the owner's local artifact (NFR-4) — never from asserting personal values.
/// </summary>
public static class DndBeyondSheetLayout
{
    /// <summary>A caption-relative value-region offset, in multiples of the caption box width/height.</summary>
    /// <param name="Dx">X offset as a multiple of caption width.</param>
    /// <param name="Dy">Y offset as a multiple of caption height (positive = DOWN the page).</param>
    /// <param name="Wx">Region width as a multiple of caption width.</param>
    /// <param name="Wy">Region height as a multiple of caption height.</param>
    public readonly record struct Offset(double Dx, double Dy, double Wx, double Wy);

    // Ability score sits in the circle directly below each ability caption.
    private static readonly Offset AbilityOffset = new(0.0, 1.1, 1.0, 1.6);

    // Stat boxes sit just below/around their caption.
    private static readonly Offset ArmorOffset = new(0.0, 0.2, 1.0, 1.4);
    private static readonly Offset HitPointsOffset = new(0.0, 0.2, 1.4, 1.4);
    private static readonly Offset SpeedOffset = new(0.0, 0.2, 1.0, 1.4);
    private static readonly Offset ProficiencyBonusOffset = new(0.0, 0.2, 1.0, 1.4);

    // Text values on the line below the caption.
    private static readonly Offset CharacterNameOffset = new(0.0, 1.0, 3.0, 1.2);
    private static readonly Offset ClassLevelOffset = new(0.0, 1.0, 3.0, 1.2);
    private static readonly Offset SpeciesOrRaceOffset = new(0.0, 1.0, 2.0, 1.2);
    private static readonly Offset BackgroundOffset = new(0.0, 1.0, 2.0, 1.2);

    /// <summary>
    /// The in-scope caption anchors and their caption-relative value-region offsets (C.3a). Keys
    /// are the verified caption tokens shared with <see cref="DndBeyondPdfHints"/>. AC uses the
    /// <c>ARMOR</c> token; HP uses the <c>HIT POINTS</c> token (the max-HP box, disambiguated by
    /// position upstream). <c>CLASS &amp; LEVEL</c> is included so its value region is OCR'd and
    /// split in the mapper.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Offset> Offsets =
        new Dictionary<string, Offset>(StringComparer.OrdinalIgnoreCase)
        {
            [DndBeyondPdfHints.StrengthCaption] = AbilityOffset,
            [DndBeyondPdfHints.DexterityCaption] = AbilityOffset,
            [DndBeyondPdfHints.ConstitutionCaption] = AbilityOffset,
            [DndBeyondPdfHints.IntelligenceCaption] = AbilityOffset,
            [DndBeyondPdfHints.WisdomCaption] = AbilityOffset,
            [DndBeyondPdfHints.CharismaCaption] = AbilityOffset,
            [DndBeyondPdfHints.ArmorCaption] = ArmorOffset,
            [DndBeyondPdfHints.HitPointsCaption] = HitPointsOffset,
            [DndBeyondPdfHints.SpeedCaption] = SpeedOffset,
            [DndBeyondPdfHints.ProficiencyBonusCaption] = ProficiencyBonusOffset,
            [DndBeyondPdfHints.CharacterNameCaption] = CharacterNameOffset,
            [DndBeyondPdfHints.ClassLevelCaption] = ClassLevelOffset,
            [DndBeyondPdfHints.SpeciesCaption] = SpeciesOrRaceOffset,
            [DndBeyondPdfHints.RaceCaption] = SpeciesOrRaceOffset,
            [DndBeyondPdfHints.BackgroundCaption] = BackgroundOffset,
        };

    /// <summary>
    /// Normalized-absolute value regions (TOP-DOWN, 0..1) for captions that may be rasterized rather
    /// than present in the text layer. Keyed off the detected page proportions. These are coarse
    /// defaults derived from the DDB template layout; an anchor with neither a caption box nor a
    /// fallback entry yields no region (flagged, never guessed).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, OcrRegion> AbsoluteFallbacks =
        new Dictionary<string, OcrRegion>(StringComparer.OrdinalIgnoreCase)
        {
            [DndBeyondPdfHints.StrengthCaption] = new(DndBeyondPdfHints.StrengthCaption, 0.03, 0.26, 0.11, 0.07),
            [DndBeyondPdfHints.DexterityCaption] = new(DndBeyondPdfHints.DexterityCaption, 0.03, 0.37, 0.11, 0.07),
            [DndBeyondPdfHints.ConstitutionCaption] = new(DndBeyondPdfHints.ConstitutionCaption, 0.03, 0.48, 0.11, 0.07),
            [DndBeyondPdfHints.IntelligenceCaption] = new(DndBeyondPdfHints.IntelligenceCaption, 0.03, 0.59, 0.11, 0.07),
            [DndBeyondPdfHints.WisdomCaption] = new(DndBeyondPdfHints.WisdomCaption, 0.03, 0.70, 0.11, 0.07),
            [DndBeyondPdfHints.CharismaCaption] = new(DndBeyondPdfHints.CharismaCaption, 0.03, 0.81, 0.11, 0.07),
            [DndBeyondPdfHints.ArmorCaption] = new(DndBeyondPdfHints.ArmorCaption, 0.18, 0.20, 0.10, 0.06),
            [DndBeyondPdfHints.HitPointsCaption] = new(DndBeyondPdfHints.HitPointsCaption, 0.30, 0.20, 0.14, 0.06),
            [DndBeyondPdfHints.SpeedCaption] = new(DndBeyondPdfHints.SpeedCaption, 0.18, 0.33, 0.10, 0.06),
            [DndBeyondPdfHints.ProficiencyBonusCaption] = new(DndBeyondPdfHints.ProficiencyBonusCaption, 0.30, 0.33, 0.10, 0.06),
            [DndBeyondPdfHints.CharacterNameCaption] = new(DndBeyondPdfHints.CharacterNameCaption, 0.18, 0.08, 0.30, 0.05),
            [DndBeyondPdfHints.ClassLevelCaption] = new(DndBeyondPdfHints.ClassLevelCaption, 0.03, 0.04, 0.30, 0.05),
            [DndBeyondPdfHints.SpeciesCaption] = new(DndBeyondPdfHints.SpeciesCaption, 0.40, 0.08, 0.20, 0.05),
            [DndBeyondPdfHints.RaceCaption] = new(DndBeyondPdfHints.RaceCaption, 0.40, 0.08, 0.20, 0.05),
            [DndBeyondPdfHints.BackgroundCaption] = new(DndBeyondPdfHints.BackgroundCaption, 0.62, 0.08, 0.20, 0.05),
        };

    /// <summary>
    /// Builds the value <see cref="OcrRegion"/> set from the resolved caption boxes. For each
    /// in-scope anchor: if a caption box is present, computes the caption-relative region (clamped
    /// to [0,1]); otherwise falls back to <see cref="AbsoluteFallbacks"/>; otherwise emits nothing
    /// for that anchor. Each region's <see cref="OcrRegion.Key"/> is the caption token so the mapper
    /// can route it via <see cref="DndBeyondPdfHints.CaptionMap"/>.
    /// </summary>
    /// <param name="captionBoxes">
    /// Resolved caption boxes keyed by the verified caption token (normalized). May omit anchors
    /// whose caption was not found in the text layer.
    /// </param>
    public static IReadOnlyList<OcrRegion> BuildRegions(IReadOnlyDictionary<string, CaptionBox> captionBoxes)
    {
        ArgumentNullException.ThrowIfNull(captionBoxes);

        var regions = new List<OcrRegion>(Offsets.Count);

        foreach (var (caption, offset) in Offsets)
        {
            if (captionBoxes.TryGetValue(caption, out var box))
            {
                regions.Add(RegionFromCaptionBox(caption, box, offset));
            }
            else if (AbsoluteFallbacks.TryGetValue(caption, out var fallback))
            {
                regions.Add(fallback);
            }
            // else: anchor unresolvable → no region → the field is flagged downstream, never guessed.
        }

        return regions;
    }

    /// <summary>Computes a single value region for one caption box + offset, clamped to [0,1].</summary>
    public static OcrRegion RegionFromCaptionBox(string caption, CaptionBox box, Offset offset)
    {
        var x = box.X + offset.Dx * box.Width;
        var y = box.Y + offset.Dy * box.Height;
        var w = offset.Wx * box.Width;
        var h = offset.Wy * box.Height;

        // Clamp the rectangle into the page. Keep a non-negative width/height after clamping.
        var x0 = Clamp01(x);
        var y0 = Clamp01(y);
        var x1 = Clamp01(x + w);
        var y1 = Clamp01(y + h);

        return new OcrRegion(caption, x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}

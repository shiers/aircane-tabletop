using Aircane.Application.Characters.Import;

namespace Aircane.Application.Characters;

/// <summary>
/// The result of attempting to extract a character from a PDF file.
/// Contains raw extracted fields, a best-effort mapped character, unmapped fields,
/// and diagnostic information.
/// </summary>
public sealed record PdfCharacterExtractionResult
{
    /// <summary>
    /// Raw field name → value pairs extracted from the PDF (form fields or text heuristics).
    /// Keys are the original field names as found in the PDF.
    /// </summary>
    public required Dictionary<string, string> ExtractedFields { get; init; }

    /// <summary>
    /// Best-effort mapped <see cref="CanonicalCharacter"/> built from the extracted fields.
    /// <c>null</c> when no fields could be mapped or when <see cref="IsOcrRequired"/> is <c>true</c>.
    /// </summary>
    public CanonicalCharacter? MappedCharacter { get; init; }

    /// <summary>
    /// Fields that were extracted but could not be mapped to a canonical character property.
    /// Useful for the review UI to surface unknown fields to the user.
    /// </summary>
    public required Dictionary<string, string> UnmappedFields { get; init; }

    /// <summary>
    /// <c>true</c> when the PDF contains no extractable text or form fields,
    /// indicating that OCR would be required to read it.
    /// The MVP does not support OCR; the caller should surface a user-facing warning.
    /// </summary>
    public bool IsOcrRequired { get; init; }

    /// <summary>
    /// Non-fatal warnings produced during extraction or mapping,
    /// e.g. "STR value 'abc' is not a valid integer - defaulting to 10."
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Canonical <c>ApplyMapping</c> paths that must be flagged for user review before the draft is
    /// considered final. On the OCR path (<see cref="Import.DndBeyondOcrMapper"/>) EVERY OCR-mapped
    /// field is listed here (FR-4.3) — including fields left at their canonical default because the
    /// OCR text could not be parsed (default-and-flag; never a fabricated value). Defaults to empty
    /// so the non-OCR extractor branches compile and serialize unchanged.
    /// </summary>
    public IReadOnlyCollection<string> RequiresReviewPaths { get; init; } = [];

    /// <summary>
    /// True when a D&amp;D Beyond printable sheet was detected (signature match) but the OCR stack
    /// is unavailable, so the values (rasterized pixels) could not be read. Lets the controller emit
    /// the actionable "OCR required but unavailable" 422 instead of the generic empty-PDF message.
    /// </summary>
    public bool OcrUnavailableForDdb { get; init; }

    /// <summary>
    /// The ruleset detected from the DDB ancestry caption (SPECIES ⇒ 2024, RACE ⇒ 2014). On a DDB
    /// OCR import the detected ruleset OVERRIDES the form-supplied ruleset for the persisted draft
    /// and is flagged requires-confirmation. <see cref="DdbRuleset.Unknown"/> when not a DDB sheet
    /// or the ancestry caption was absent (keep the form ruleset).
    /// </summary>
    public DdbRuleset DetectedRuleset { get; init; } = DdbRuleset.Unknown;
}

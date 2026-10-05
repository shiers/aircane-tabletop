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
}

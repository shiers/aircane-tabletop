namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Represents a single field from an import that could not be confidently mapped
/// to a canonical character property. Returned in the review queue for manual mapping.
/// </summary>
public sealed record UnmappedFieldDto(
    /// <summary>The original field name as found in the source (PDF form field or JSON key).</summary>
    string SourceFieldName,
    /// <summary>The raw value extracted from the source.</summary>
    string SourceValue,
    /// <summary>
    /// The canonical field path the system suggests this field maps to, e.g. "identity.name".
    /// Null when no suggestion can be made.
    /// </summary>
    string? SuggestedCanonicalField,
    /// <summary>
    /// Confidence score for the suggestion, 0.0–1.0.
    /// 0.0 means no suggestion; 1.0 means the system is certain.
    /// </summary>
    float Confidence);

/// <summary>
/// Response returned by the PDF import endpoint when one or more fields could not be
/// confidently mapped. The frontend should route the user to the field review UI.
/// </summary>
public sealed record CharacterFieldReviewDto(
    /// <summary>The ID of the draft character that was persisted and awaits review.</summary>
    Guid CharacterId,
    /// <summary>Always true when this DTO is returned — signals the frontend to show the review UI.</summary>
    bool ReviewRequired,
    /// <summary>Fields that need manual mapping before the character is considered complete.</summary>
    IReadOnlyList<UnmappedFieldDto> UnmappedFields,
    /// <summary>Non-fatal warnings produced during extraction, e.g. invalid numeric values.</summary>
    IReadOnlyList<string> Warnings);

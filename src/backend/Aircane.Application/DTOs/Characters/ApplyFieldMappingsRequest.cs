namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// A single field mapping submitted by the user during the review step.
/// Maps a source field name to a canonical character field path and the corrected value.
/// </summary>
public sealed record FieldMappingEntry(
    /// <summary>The original field name from the import source.</summary>
    string SourceFieldName,
    /// <summary>
    /// Dot-separated path to the canonical character property to set,
    /// e.g. "identity.name", "abilities.strength", "combat.armorClass".
    /// </summary>
    string CanonicalFieldPath,
    /// <summary>The value to write to the canonical field (as a string; will be coerced to the target type).</summary>
    string Value);

/// <summary>
/// Request body for PUT /api/characters/{id}/field-mappings.
/// Applies a list of user-confirmed field mappings to the character's CanonicalJson
/// and marks the character as reviewed.
/// </summary>
public sealed record ApplyFieldMappingsRequest(
    /// <summary>The field mappings to apply.</summary>
    IReadOnlyList<FieldMappingEntry> Mappings);

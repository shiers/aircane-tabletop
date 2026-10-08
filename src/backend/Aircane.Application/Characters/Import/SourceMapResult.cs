namespace Aircane.Application.Characters.Import;

/// <summary>Confidence level a mapper has in the values it produced.</summary>
public enum ImportConfidence
{
    /// <summary>The mapper recognised the format and read values from known fields.</summary>
    High,

    /// <summary>Best-effort mapping; every mapped value should be reviewed before saving.</summary>
    Low
}

/// <summary>
/// The output of a single <see cref="ICharacterSourceMapper"/>. Wraps a draft
/// <see cref="CanonicalCharacter"/> (the same shape the PDF import produces) plus the authoritative
/// set of <c>ApplyMapping</c> paths the mapper actually populated and any leftover source fields.
/// </summary>
public sealed record SourceMapResult
{
    /// <summary>The mapped draft, in the SAME shape the PDF extractor produces.</summary>
    public required CanonicalCharacter Character { get; init; }

    /// <summary>
    /// The authoritative set of <c>ApplyMapping</c> paths this mapper ACTUALLY populated, with
    /// their stringified values (e.g. <c>{"abilities.strength":"16"}</c>). This is the ONLY source
    /// of the review payload's mapped fields; it is never derived by flattening
    /// <see cref="Character"/>, because <see cref="CanonicalCharacter"/>'s non-zero defaults
    /// (abilities=10, AC=10, speed=30, proficiencyBonus=2) are indistinguishable from mapped values
    /// under a flatten. A key appears here ONLY when a usable value was read from the source.
    /// Numeric values are emitted as bare base-10 integer strings (no units/suffixes).
    /// </summary>
    public required IReadOnlyDictionary<string, string> MappedFields { get; init; }

    /// <summary>
    /// Values the mapper extracted that have NO <c>ApplyMapping</c> path (PF2e saves, class DC,
    /// perception, heritage, lores, spells, equipment, features…). Surfaced in the review UI;
    /// never passed to <c>ApplyMapping</c>. Key = human/source label, value = stringified value.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraFields { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Subset of <see cref="MappedFields"/> keys whose value the user must review even though it
    /// mapped cleanly (e.g. a derived HP value, or every best-effort low-confidence value). Every
    /// path here must also appear in <see cref="MappedFields"/>.
    /// </summary>
    public IReadOnlyCollection<string> RequiresReviewPaths { get; init; } = [];

    /// <summary>Confidence the mapper has in the values it produced.</summary>
    public ImportConfidence Confidence { get; init; } = ImportConfidence.High;

    /// <summary>"2014"/"2024" for D&amp;D 5e; "Remaster" for PF2e; null when none is implied.</summary>
    public string? Ruleset { get; init; }

    /// <summary>True when the detected ruleset should be confirmed by the user before saving.</summary>
    public bool RulesetRequiresConfirmation { get; init; }

    /// <summary>
    /// Forced game-system IDENTIFIER (not display name): e.g. "dnd-5e-2014" or
    /// "pathfinder-2e-remaster" when the source implies one; null for Roll20/Generic.
    /// </summary>
    public string? GameSystemIdentifier { get; init; }

    /// <summary>Non-fatal warnings produced during mapping.</summary>
    public IReadOnlyCollection<string> Warnings { get; init; } = [];
}

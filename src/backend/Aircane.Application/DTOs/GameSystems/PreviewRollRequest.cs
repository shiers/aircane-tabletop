namespace Aircane.Application.DTOs.GameSystems;

/// <summary>
/// Request body for previewing a dice roll using a game system's dice convention.
/// </summary>
public sealed record PreviewRollRequest(
    string Formula,
    string? ConventionName = null,
    string? ResolutionRuleName = null,
    int? TargetNumber = null);

/// <summary>
/// Response for a dice roll preview.
/// </summary>
public sealed record PreviewRollResponse(
    string Formula,
    IReadOnlyList<int> RawResults,
    IReadOnlyList<int> KeptResults,
    int Modifier,
    int Total,
    int? SuccessCount,
    string? OutcomeTier,
    IReadOnlyList<int>? ExplodedResults);

/// <summary>
/// Request body for previewing a character form rendering.
/// </summary>
public sealed record PreviewCharacterFormRequest(
    string? CharacterSchemaJson = null);

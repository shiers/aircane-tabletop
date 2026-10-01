namespace Aircane.Application.DTOs.Characters;

/// <summary>
/// Result of a JSON character import operation.
/// On success <see cref="Character"/> is populated and <see cref="Errors"/> is empty.
/// On failure <see cref="Character"/> is null and <see cref="Errors"/> contains validation messages.
/// </summary>
public sealed record CharacterImportResult(
    bool Success,
    CharacterDto? Character,
    IReadOnlyList<string> Errors)
{
    /// <summary>Creates a successful import result.</summary>
    public static CharacterImportResult Ok(CharacterDto character) =>
        new(Success: true, Character: character, Errors: []);

    /// <summary>Creates a failed import result with one or more error messages.</summary>
    public static CharacterImportResult Fail(IReadOnlyList<string> errors) =>
        new(Success: false, Character: null, Errors: errors);

    /// <summary>Creates a failed import result with a single error message.</summary>
    public static CharacterImportResult Fail(string error) =>
        Fail([error]);
}

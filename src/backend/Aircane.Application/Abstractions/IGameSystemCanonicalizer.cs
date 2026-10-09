namespace Aircane.Application.Abstractions;

/// <summary>
/// Canonicalizes a free-text game system value against the known
/// <see cref="Aircane.Domain.Entities.GameSystems.GameSystemDefinition"/>s so that different
/// spellings of the same system (e.g. "Pathfinder 2e" vs "pathfinder-2e-remaster") converge on a
/// single canonical display name.
/// </summary>
/// <remarks>
/// Matching is intentionally conservative (name/identifier only, case- and whitespace-insensitive).
/// Input that does not match any known definition is preserved (trimmed) so users can still record
/// systems that have no <c>GameSystemDefinition</c> yet.
/// </remarks>
public interface IGameSystemCanonicalizer
{
    /// <summary>
    /// Returns the canonical game system display name for <paramref name="input"/>.
    /// If the input matches an active definition by <c>Name</c> or <c>Identifier</c>
    /// (ignoring case and surrounding whitespace), the definition's <c>Name</c> is returned.
    /// Otherwise the trimmed input is returned unchanged. Null/empty input is returned as-is.
    /// </summary>
    Task<string> CanonicalizeAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every known spelling of the game system <paramref name="input"/> refers to: the
    /// matched definition's <c>Name</c>, <c>Identifier</c>, and all of its aliases. Used to filter
    /// documents whose stored game system predates canonicalization (e.g. built-in content tagged
    /// "D&amp;D 5e"). If no definition matches, returns just the trimmed input.
    /// </summary>
    Task<IReadOnlyList<string>> GetEquivalentNamesAsync(string input, CancellationToken cancellationToken = default);
}

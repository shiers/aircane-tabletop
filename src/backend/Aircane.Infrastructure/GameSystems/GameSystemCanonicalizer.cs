using Aircane.Application.Abstractions;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.GameSystems;

/// <summary>
/// Canonicalizes free-text game system values against active
/// <see cref="Aircane.Domain.Entities.GameSystems.GameSystemDefinition"/>s by matching on
/// <c>Name</c> or <c>Identifier</c>, case- and whitespace-insensitively. See
/// <see cref="IGameSystemCanonicalizer"/> for the matching contract.
/// </summary>
public sealed class GameSystemCanonicalizer : IGameSystemCanonicalizer
{
    private readonly AircaneDbContext _db;

    public GameSystemCanonicalizer(AircaneDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<string> CanonicalizeAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var normalizedInput = Normalize(input);

        // Pull the small set of active definitions (name + identifier) and match in memory.
        // The comparison collapses case and internal/leading/trailing whitespace, which the
        // provider-side string functions cannot express portably.
        var candidates = await _db.GameSystemDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .Select(d => new { d.Name, d.Identifier })
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (Normalize(candidate.Name) == normalizedInput ||
                Normalize(candidate.Identifier) == normalizedInput)
            {
                return candidate.Name;
            }
        }

        // Fall back to alias matching: a stored GameSystemAlias may map a common short-hand
        // (e.g. "D&D 5e", "PF2e") to a definition whose canonical name we then return.
        var aliasMatch = await _db.GameSystemAliases
            .AsNoTracking()
            .Where(a => a.GameSystemDefinition != null && a.GameSystemDefinition.IsActive)
            .Select(a => new { a.Alias, DefinitionName = a.GameSystemDefinition!.Name })
            .ToListAsync(cancellationToken);

        foreach (var alias in aliasMatch)
        {
            if (Normalize(alias.Alias) == normalizedInput)
                return alias.DefinitionName;
        }

        // No known definition matched: preserve the user's value (trimmed) so systems without a
        // GameSystemDefinition can still be recorded.
        return input.Trim();
    }

    /// <summary>
    /// Collapses a value to a comparison key: trimmed, lower-cased, with all runs of whitespace
    /// reduced to a single space. This makes "Pathfinder 2e", "pathfinder  2E", and
    /// " Pathfinder 2e " compare equal.
    /// </summary>
    private static string Normalize(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return string.Join(' ', trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

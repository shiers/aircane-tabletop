using Aircane.Application.DTOs.Library;

namespace Aircane.Application.Abstractions;

/// <summary>
/// Manages custom aliases for game-system definitions (view/add/remove). Aliases let hosts map
/// short-hand names (e.g. "D&amp;D 5e", "PF2e") to a canonical definition for import canonicalization.
/// </summary>
public interface IGameSystemAliasService
{
    /// <summary>Lists all aliases for a definition.</summary>
    Task<IReadOnlyList<GameSystemAliasDto>> ListAsync(Guid definitionId, CancellationToken ct = default);

    /// <summary>
    /// Adds an alias to a definition. No-ops (returns the existing one) if the alias already
    /// exists for that definition (case-insensitive). Throws <see cref="KeyNotFoundException"/>
    /// if the definition does not exist.
    /// </summary>
    Task<GameSystemAliasDto> AddAsync(Guid definitionId, string alias, CancellationToken ct = default);

    /// <summary>Removes an alias by id. No-op if it does not exist.</summary>
    Task RemoveAsync(Guid aliasId, CancellationToken ct = default);
}

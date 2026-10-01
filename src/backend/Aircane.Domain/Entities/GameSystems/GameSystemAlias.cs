using Aircane.Domain.Common;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// A short-hand or common alternative name for a <see cref="GameSystemDefinition"/> (e.g. "D&amp;D 5e",
/// "PF2e"). Used by the canonicalizer to resolve free-text game-system input to a canonical
/// definition when the exact name/identifier does not match.
/// </summary>
public class GameSystemAlias : EntityBase
{
    /// <summary>Foreign key to the aliased <see cref="GameSystemDefinition"/>.</summary>
    public Guid GameSystemDefinitionId { get; set; }

    /// <summary>Navigation property to the aliased definition.</summary>
    public GameSystemDefinition? GameSystemDefinition { get; set; }

    /// <summary>The alias text (e.g. "D&amp;D 5e", "PF2e"). Matched case- and whitespace-insensitively.</summary>
    public string Alias { get; set; }

    public GameSystemAlias(Guid gameSystemDefinitionId, string alias)
    {
        GameSystemDefinitionId = gameSystemDefinitionId;
        Alias = alias;
    }

    // EF Core constructor
    private GameSystemAlias() : base()
    {
        Alias = string.Empty;
    }
}

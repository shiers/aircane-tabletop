using Aircane.Domain.Common;

namespace Aircane.Domain.Entities.GameSystems;

/// <summary>
/// A versioned snapshot of a Game System Definition. Each update to a definition
/// creates a new version; campaigns pin to a specific version for stability.
/// </summary>
public class GameSystemDefinitionVersion : EntityBase
{
    /// <summary>FK to the parent Game System Definition.</summary>
    public Guid GameSystemDefinitionId { get; init; }

    /// <summary>Semantic version string (e.g., "1.0.0", "1.1.0").</summary>
    public string Version { get; set; }

    /// <summary>Full definition JSON snapshot at this version.</summary>
    public string DefinitionJson { get; set; }

    /// <summary>Navigation property to the parent definition.</summary>
    public GameSystemDefinition? GameSystemDefinition { get; set; }

    public GameSystemDefinitionVersion(
        Guid gameSystemDefinitionId,
        string version,
        string definitionJson)
    {
        GameSystemDefinitionId = gameSystemDefinitionId;
        Version = version;
        DefinitionJson = definitionJson;
    }

    // EF Core constructor
    private GameSystemDefinitionVersion() : base()
    {
        Version = string.Empty;
        DefinitionJson = "{}";
    }
}

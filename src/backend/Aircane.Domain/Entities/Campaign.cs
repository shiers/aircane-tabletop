using Aircane.Domain.Common;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;

namespace Aircane.Domain.Entities;

/// <summary>
/// A campaign groups sessions, characters, and adventures under a shared ruleset and AI configuration.
/// </summary>
public class Campaign : EntityBase
{
    public string Name { get; set; }
    public string GameSystem { get; init; }
    public string Ruleset { get; init; }
    public AiRole AiRole { get; set; }
    public AiAuthority AiAuthority { get; set; }
    public Guid? ActiveAdventureId { get; set; }

    /// <summary>FK to the bound Game System Definition. Null for legacy campaigns not yet migrated.</summary>
    public Guid? GameSystemDefinitionId { get; set; }

    /// <summary>FK to the pinned version of the Game System Definition. Null means use latest.</summary>
    public Guid? GameSystemDefinitionVersionId { get; set; }

    /// <summary>Navigation property to the bound Game System Definition.</summary>
    public GameSystemDefinition? GameSystemDefinition { get; set; }

    /// <summary>Navigation property to the pinned version.</summary>
    public GameSystemDefinitionVersion? GameSystemDefinitionVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Campaign(
        string name,
        string gameSystem,
        string ruleset,
        AiRole aiRole = AiRole.Assistant,
        AiAuthority aiAuthority = AiAuthority.SuggestOnly,
        Guid? activeAdventureId = null)
    {
        Name = name;
        GameSystem = gameSystem;
        Ruleset = ruleset;
        AiRole = aiRole;
        AiAuthority = aiAuthority;
        ActiveAdventureId = activeAdventureId;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private Campaign() : base()
    {
        Name = string.Empty;
        GameSystem = string.Empty;
        Ruleset = string.Empty;
    }
}

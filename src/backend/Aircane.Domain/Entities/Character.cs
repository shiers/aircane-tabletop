using Aircane.Domain.Common;
using Aircane.Domain.Entities.GameSystems;

namespace Aircane.Domain.Entities;

/// <summary>
/// A durable player character sheet. Stores both the canonical normalized data
/// and a runtime state snapshot as JSON blobs.
/// </summary>
public class Character : EntityBase
{
    public Guid? CampaignId { get; init; }
    public Guid? OwnerParticipantId { get; set; }
    public string Name { get; set; }
    public string GameSystem { get; init; }
    public string Ruleset { get; init; }
    public int Level { get; set; }

    /// <summary>FK to the Game System Definition this character uses. Null for legacy characters.</summary>
    public Guid? GameSystemDefinitionId { get; set; }

    /// <summary>Navigation property to the bound Game System Definition.</summary>
    public GameSystemDefinition? GameSystemDefinition { get; set; }

    /// <summary>Full character data as JSON (canonical schema).</summary>
    public string CanonicalJson { get; set; }

    /// <summary>Runtime state as JSON (HP, conditions, resources, etc.).</summary>
    public string CurrentStateJson { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Character(
        string name,
        string gameSystem,
        string ruleset,
        int level,
        string canonicalJson,
        string currentStateJson,
        Guid? campaignId = null,
        Guid? ownerParticipantId = null)
    {
        Name = name;
        GameSystem = gameSystem;
        Ruleset = ruleset;
        Level = level;
        CanonicalJson = canonicalJson;
        CurrentStateJson = currentStateJson;
        CampaignId = campaignId;
        OwnerParticipantId = ownerParticipantId;
        UpdatedAt = CreatedAt;
    }

    // EF Core constructor
    private Character() : base()
    {
        Name = string.Empty;
        GameSystem = string.Empty;
        Ruleset = string.Empty;
        CanonicalJson = "{}";
        CurrentStateJson = "{}";
    }
}

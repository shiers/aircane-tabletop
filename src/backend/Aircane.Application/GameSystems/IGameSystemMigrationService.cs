namespace Aircane.Application.GameSystems;

/// <summary>
/// Migrates existing campaigns and characters to use Game System Definition bindings.
/// </summary>
public interface IGameSystemMigrationService
{
    /// <summary>
    /// Migrates all existing campaigns and characters that have string-based GameSystem/Ruleset
    /// values to use the appropriate GameSystemDefinitionId foreign key.
    /// </summary>
    Task<MigrationReport> MigrateExistingDataAsync(CancellationToken ct = default);
}

/// <summary>
/// Report of how many entities were migrated during the data migration.
/// </summary>
public record MigrationReport
{
    /// <summary>Number of campaigns migrated to the D&D 5e 2014 definition.</summary>
    public int CampaignsMigrated { get; init; }

    /// <summary>Number of characters migrated to the D&D 5e 2014 definition.</summary>
    public int CharactersMigrated { get; init; }

    /// <summary>Number of campaigns bound to the Generic Freeform fallback.</summary>
    public int CampaignsBoundToFreeform { get; init; }

    /// <summary>Number of characters bound to the Generic Freeform fallback.</summary>
    public int CharactersBoundToFreeform { get; init; }
}

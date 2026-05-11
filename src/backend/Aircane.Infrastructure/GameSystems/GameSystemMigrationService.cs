using Aircane.Application.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aircane.Infrastructure.GameSystems;

/// <summary>
/// Migrates existing campaigns and characters from string-based GameSystem/Ruleset
/// to the new GameSystemDefinitionId foreign key binding.
/// </summary>
public class GameSystemMigrationService : IGameSystemMigrationService
{
    private readonly AircaneDbContext _db;

    public GameSystemMigrationService(AircaneDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<MigrationReport> MigrateExistingDataAsync(CancellationToken ct = default)
    {
        var dnd5eId = DnD5e2014Seed.DefinitionId;
        var freeformId = GenericFreeformSeed.DefinitionId;

        // Migrate campaigns with D&D 5e / 2014 to the built-in definition
        var dndCampaigns = await _db.Campaigns
            .Where(c => c.GameSystemDefinitionId == null
                        && c.GameSystem == "D&D 5e"
                        && c.Ruleset == "2014")
            .ToListAsync(ct);

        foreach (var campaign in dndCampaigns)
        {
            campaign.GameSystemDefinitionId = dnd5eId;
        }

        // Migrate campaigns with unrecognized GameSystem to Generic Freeform
        var unrecognizedCampaigns = await _db.Campaigns
            .Where(c => c.GameSystemDefinitionId == null
                        && !(c.GameSystem == "D&D 5e" && c.Ruleset == "2014"))
            .ToListAsync(ct);

        foreach (var campaign in unrecognizedCampaigns)
        {
            campaign.GameSystemDefinitionId = freeformId;
        }

        // Migrate characters with D&D 5e / 2014 to the built-in definition
        var dndCharacters = await _db.Characters
            .Where(c => c.GameSystemDefinitionId == null
                        && c.GameSystem == "D&D 5e"
                        && c.Ruleset == "2014")
            .ToListAsync(ct);

        foreach (var character in dndCharacters)
        {
            character.GameSystemDefinitionId = dnd5eId;
        }

        // Migrate characters with unrecognized GameSystem to Generic Freeform
        var unrecognizedCharacters = await _db.Characters
            .Where(c => c.GameSystemDefinitionId == null
                        && !(c.GameSystem == "D&D 5e" && c.Ruleset == "2014"))
            .ToListAsync(ct);

        foreach (var character in unrecognizedCharacters)
        {
            character.GameSystemDefinitionId = freeformId;
        }

        await _db.SaveChangesAsync(ct);

        return new MigrationReport
        {
            CampaignsMigrated = dndCampaigns.Count,
            CharactersMigrated = dndCharacters.Count,
            CampaignsBoundToFreeform = unrecognizedCampaigns.Count,
            CharactersBoundToFreeform = unrecognizedCharacters.Count
        };
    }
}

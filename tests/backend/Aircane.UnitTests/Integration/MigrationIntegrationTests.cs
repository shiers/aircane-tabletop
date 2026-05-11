using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.Integration;

/// <summary>
/// Integration tests for the data migration from string-based GameSystem/Ruleset
/// to GameSystemDefinitionId foreign key binding.
/// Tests comprehensive scenarios including mixed data, edge cases, and data preservation.
/// </summary>
public class MigrationIntegrationTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly GameSystemMigrationService _service;

    public MigrationIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: $"MigrationIntegration_{Guid.NewGuid()}")
            .Options;

        _db = new AircaneDbContext(options);
        SeedBuiltInDefinitions();
        _service = new GameSystemMigrationService(_db);
    }

    private void SeedBuiltInDefinitions()
    {
        var dnd5e = DnD5e2014Seed.Create();
        dnd5e.DefinitionJson = "{}";
        _db.GameSystemDefinitions.Add(dnd5e);

        var freeform = GenericFreeformSeed.Create();
        freeform.DefinitionJson = "{}";
        _db.GameSystemDefinitions.Add(freeform);

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // ── Comprehensive Migration Scenarios ─────────────────────────────────────

    [Fact]
    public async Task Migration_MultipleDnD5eCampaigns_AllBoundToBuiltInDefinition()
    {
        // Arrange: seed multiple D&D 5e campaigns
        var campaigns = new[]
        {
            new Campaign("Dragon Heist", "D&D 5e", "2014"),
            new Campaign("Curse of Strahd", "D&D 5e", "2014"),
            new Campaign("Tomb of Annihilation", "D&D 5e", "2014"),
            new Campaign("Storm King's Thunder", "D&D 5e", "2014"),
        };
        _db.Campaigns.AddRange(campaigns);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(4, report.CampaignsMigrated);
        Assert.Equal(0, report.CampaignsBoundToFreeform);

        foreach (var campaign in campaigns)
        {
            var migrated = await _db.Campaigns.FindAsync(campaign.Id);
            Assert.Equal(DnD5e2014Seed.DefinitionId, migrated!.GameSystemDefinitionId);
        }
    }

    [Fact]
    public async Task Migration_MultipleUnrecognizedSystems_AllBoundToFreeform()
    {
        // Arrange: seed campaigns with various unrecognized systems
        var campaigns = new[]
        {
            new Campaign("Shadowrun Campaign", "Shadowrun", "6e"),
            new Campaign("FATE Campaign", "FATE", "Core"),
            new Campaign("Call of Cthulhu", "CoC", "7e"),
            new Campaign("Savage Worlds", "Savage Worlds", "SWADE"),
        };
        _db.Campaigns.AddRange(campaigns);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(4, report.CampaignsBoundToFreeform);

        foreach (var campaign in campaigns)
        {
            var migrated = await _db.Campaigns.FindAsync(campaign.Id);
            Assert.Equal(GenericFreeformSeed.DefinitionId, migrated!.GameSystemDefinitionId);
        }
    }

    [Fact]
    public async Task Migration_MixedCampaignsAndCharacters_CorrectlyBindsAll()
    {
        // Arrange: mix of D&D 5e and unrecognized systems
        var dndCampaign1 = new Campaign("DnD Campaign 1", "D&D 5e", "2014");
        var dndCampaign2 = new Campaign("DnD Campaign 2", "D&D 5e", "2014");
        var fateCampaign = new Campaign("FATE Campaign", "FATE", "Core");
        var srCampaign = new Campaign("SR Campaign", "Shadowrun", "6e");

        var dndChar1 = new Character("Fighter", "D&D 5e", "2014", 5, "{\"str\":16}", "{\"hp\":45}");
        var dndChar2 = new Character("Wizard", "D&D 5e", "2014", 7, "{\"int\":18}", "{\"hp\":32}");
        var fateChar = new Character("Aspect", "FATE", "Core", 1, "{\"high_concept\":\"test\"}", "{}");

        _db.Campaigns.AddRange(dndCampaign1, dndCampaign2, fateCampaign, srCampaign);
        _db.Characters.AddRange(dndChar1, dndChar2, fateChar);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(2, report.CampaignsMigrated);
        Assert.Equal(2, report.CampaignsBoundToFreeform);
        Assert.Equal(2, report.CharactersMigrated);
        Assert.Equal(1, report.CharactersBoundToFreeform);

        // Verify D&D campaigns bound correctly
        Assert.Equal(DnD5e2014Seed.DefinitionId, (await _db.Campaigns.FindAsync(dndCampaign1.Id))!.GameSystemDefinitionId);
        Assert.Equal(DnD5e2014Seed.DefinitionId, (await _db.Campaigns.FindAsync(dndCampaign2.Id))!.GameSystemDefinitionId);

        // Verify unrecognized campaigns bound to freeform
        Assert.Equal(GenericFreeformSeed.DefinitionId, (await _db.Campaigns.FindAsync(fateCampaign.Id))!.GameSystemDefinitionId);
        Assert.Equal(GenericFreeformSeed.DefinitionId, (await _db.Campaigns.FindAsync(srCampaign.Id))!.GameSystemDefinitionId);

        // Verify characters
        Assert.Equal(DnD5e2014Seed.DefinitionId, (await _db.Characters.FindAsync(dndChar1.Id))!.GameSystemDefinitionId);
        Assert.Equal(DnD5e2014Seed.DefinitionId, (await _db.Characters.FindAsync(dndChar2.Id))!.GameSystemDefinitionId);
        Assert.Equal(GenericFreeformSeed.DefinitionId, (await _db.Characters.FindAsync(fateChar.Id))!.GameSystemDefinitionId);
    }

    [Fact]
    public async Task Migration_PreservesAllCampaignFields_Unchanged()
    {
        // Arrange: campaign with all fields populated
        var campaign = new Campaign("My Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        var originalName = campaign.Name;
        var originalGameSystem = campaign.GameSystem;
        var originalRuleset = campaign.Ruleset;
        var originalAiRole = campaign.AiRole;
        var originalAiAuthority = campaign.AiAuthority;
        var originalCreatedAt = campaign.CreatedAt;

        // Act
        await _service.MigrateExistingDataAsync();

        // Assert: all fields preserved
        var migrated = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal(originalName, migrated!.Name);
        Assert.Equal(originalGameSystem, migrated.GameSystem);
        Assert.Equal(originalRuleset, migrated.Ruleset);
        Assert.Equal(originalAiRole, migrated.AiRole);
        Assert.Equal(originalAiAuthority, migrated.AiAuthority);
        Assert.Equal(originalCreatedAt, migrated.CreatedAt);
        // Only GameSystemDefinitionId should be set
        Assert.Equal(DnD5e2014Seed.DefinitionId, migrated.GameSystemDefinitionId);
    }

    [Fact]
    public async Task Migration_PreservesAllCharacterFields_Unchanged()
    {
        // Arrange: character with all fields populated
        var canonicalJson = """{"str":18,"dex":14,"con":16,"int":10,"wis":12,"cha":8}""";
        var stateJson = """{"hp":52,"conditions":["Poisoned"],"spell_slots_1":3}""";
        var character = new Character("Thorin Ironforge", "D&D 5e", "2014", 8, canonicalJson, stateJson);
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        await _service.MigrateExistingDataAsync();

        // Assert: all fields preserved
        var migrated = await _db.Characters.FindAsync(character.Id);
        Assert.Equal("Thorin Ironforge", migrated!.Name);
        Assert.Equal("D&D 5e", migrated.GameSystem);
        Assert.Equal("2014", migrated.Ruleset);
        Assert.Equal(8, migrated.Level);
        Assert.Equal(canonicalJson, migrated.CanonicalJson);
        Assert.Equal(stateJson, migrated.CurrentStateJson);
        Assert.Equal(DnD5e2014Seed.DefinitionId, migrated.GameSystemDefinitionId);
    }

    [Fact]
    public async Task Migration_AlreadyMigratedData_IsSkipped()
    {
        // Arrange: campaigns and characters already migrated
        var campaign = new Campaign("Already Done", "D&D 5e", "2014")
        {
            GameSystemDefinitionId = DnD5e2014Seed.DefinitionId
        };
        var character = new Character("Already Done", "D&D 5e", "2014", 5, "{}", "{}")
        {
            GameSystemDefinitionId = DnD5e2014Seed.DefinitionId
        };
        _db.Campaigns.Add(campaign);
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert: nothing migrated
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(0, report.CharactersMigrated);
        Assert.Equal(0, report.CampaignsBoundToFreeform);
        Assert.Equal(0, report.CharactersBoundToFreeform);
    }

    [Fact]
    public async Task Migration_Idempotent_RunningTwiceProducesSameResult()
    {
        // Arrange
        _db.Campaigns.Add(new Campaign("DnD Campaign", "D&D 5e", "2014"));
        _db.Campaigns.Add(new Campaign("FATE Campaign", "FATE", "Core"));
        _db.Characters.Add(new Character("Fighter", "D&D 5e", "2014", 5, "{}", "{}"));
        await _db.SaveChangesAsync();

        // Act: run migration twice
        var report1 = await _service.MigrateExistingDataAsync();
        var report2 = await _service.MigrateExistingDataAsync();

        // Assert: second run does nothing
        Assert.Equal(1, report1.CampaignsMigrated);
        Assert.Equal(1, report1.CampaignsBoundToFreeform);
        Assert.Equal(1, report1.CharactersMigrated);

        Assert.Equal(0, report2.CampaignsMigrated);
        Assert.Equal(0, report2.CampaignsBoundToFreeform);
        Assert.Equal(0, report2.CharactersMigrated);
    }

    [Fact]
    public async Task Migration_EmptyDatabase_ReturnsZeroCounts()
    {
        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(0, report.CharactersMigrated);
        Assert.Equal(0, report.CampaignsBoundToFreeform);
        Assert.Equal(0, report.CharactersBoundToFreeform);
    }

    [Fact]
    public async Task Migration_CaseSensitiveMatching_OnlyExactDnD5eMatches()
    {
        // Arrange: various case variations that should NOT match D&D 5e
        _db.Campaigns.Add(new Campaign("Lower Case", "d&d 5e", "2014"));
        _db.Campaigns.Add(new Campaign("Different Version", "D&D 5e", "2024"));
        _db.Campaigns.Add(new Campaign("Partial Match", "D&D", "5e"));
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert: none match D&D 5e exactly, all go to freeform
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(3, report.CampaignsBoundToFreeform);
    }
}

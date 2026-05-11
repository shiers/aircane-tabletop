using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// Unit tests for the GameSystemMigrationService.
/// Uses EF Core InMemory provider to test migration logic.
/// </summary>
public class GameSystemMigrationServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly GameSystemMigrationService _service;

    public GameSystemMigrationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: $"MigrationTest_{Guid.NewGuid()}")
            .Options;

        _db = new AircaneDbContext(options);
        SeedBuiltInDefinitions();
        _service = new GameSystemMigrationService(_db);
    }

    private void SeedBuiltInDefinitions()
    {
        var dnd5e = DnD5e2014Seed.Create();
        dnd5e.DefinitionJson = "{}"; // Simplified for InMemory tests
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

    [Fact]
    public async Task MigrateExistingData_BindsDnD5eCampaigns_ToBuiltInDefinition()
    {
        // Arrange
        var campaign = new Campaign("Test Campaign", "D&D 5e", "2014");
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal(DnD5e2014Seed.DefinitionId, migrated!.GameSystemDefinitionId);
        Assert.Equal(1, report.CampaignsMigrated);
    }

    [Fact]
    public async Task MigrateExistingData_BindsDnD5eCharacters_ToBuiltInDefinition()
    {
        // Arrange
        var character = new Character("Gandalf", "D&D 5e", "2014", 10, "{\"str\":16}", "{\"hp\":45}");
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Characters.FindAsync(character.Id);
        Assert.Equal(DnD5e2014Seed.DefinitionId, migrated!.GameSystemDefinitionId);
        Assert.Equal(1, report.CharactersMigrated);
    }

    [Fact]
    public async Task MigrateExistingData_BindsUnrecognizedCampaigns_ToFreeform()
    {
        // Arrange
        var campaign = new Campaign("Shadowrun Campaign", "Shadowrun", "6e");
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal(GenericFreeformSeed.DefinitionId, migrated!.GameSystemDefinitionId);
        Assert.Equal(1, report.CampaignsBoundToFreeform);
    }

    [Fact]
    public async Task MigrateExistingData_BindsUnrecognizedCharacters_ToFreeform()
    {
        // Arrange
        var character = new Character("Runner", "Shadowrun", "6e", 5, "{\"body\":4}", "{\"stun\":0}");
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Characters.FindAsync(character.Id);
        Assert.Equal(GenericFreeformSeed.DefinitionId, migrated!.GameSystemDefinitionId);
        Assert.Equal(1, report.CharactersBoundToFreeform);
    }

    [Fact]
    public async Task MigrateExistingData_PreservesAllCampaignFields()
    {
        // Arrange
        var campaign = new Campaign("My Campaign", "D&D 5e", "2014", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Act
        await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal("My Campaign", migrated!.Name);
        Assert.Equal("D&D 5e", migrated.GameSystem);
        Assert.Equal("2014", migrated.Ruleset);
        Assert.Equal(AiRole.FullDm, migrated.AiRole);
        Assert.Equal(AiAuthority.FullSessionControl, migrated.AiAuthority);
    }

    [Fact]
    public async Task MigrateExistingData_PreservesAllCharacterFields()
    {
        // Arrange
        var canonicalJson = "{\"str\":18,\"dex\":14,\"con\":16}";
        var stateJson = "{\"hp\":52,\"conditions\":[]}";
        var character = new Character("Thorin", "D&D 5e", "2014", 8, canonicalJson, stateJson);
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        await _service.MigrateExistingDataAsync();

        // Assert
        var migrated = await _db.Characters.FindAsync(character.Id);
        Assert.Equal("Thorin", migrated!.Name);
        Assert.Equal(8, migrated.Level);
        Assert.Equal(canonicalJson, migrated.CanonicalJson);
        Assert.Equal(stateJson, migrated.CurrentStateJson);
        Assert.Equal("D&D 5e", migrated.GameSystem);
        Assert.Equal("2014", migrated.Ruleset);
    }

    [Fact]
    public async Task MigrateExistingData_SkipsAlreadyMigratedCampaigns()
    {
        // Arrange - campaign already has a definition ID
        var campaign = new Campaign("Already Migrated", "D&D 5e", "2014");
        campaign.GameSystemDefinitionId = DnD5e2014Seed.DefinitionId;
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(0, report.CampaignsBoundToFreeform);
    }

    [Fact]
    public async Task MigrateExistingData_SkipsAlreadyMigratedCharacters()
    {
        // Arrange - character already has a definition ID
        var character = new Character("Already Done", "D&D 5e", "2014", 5, "{}", "{}");
        character.GameSystemDefinitionId = DnD5e2014Seed.DefinitionId;
        _db.Characters.Add(character);
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(0, report.CharactersMigrated);
        Assert.Equal(0, report.CharactersBoundToFreeform);
    }

    [Fact]
    public async Task MigrateExistingData_HandlesMixedData()
    {
        // Arrange
        _db.Campaigns.Add(new Campaign("DnD Campaign 1", "D&D 5e", "2014"));
        _db.Campaigns.Add(new Campaign("DnD Campaign 2", "D&D 5e", "2014"));
        _db.Campaigns.Add(new Campaign("FATE Campaign", "FATE", "Core"));
        _db.Characters.Add(new Character("Fighter", "D&D 5e", "2014", 5, "{}", "{}"));
        _db.Characters.Add(new Character("Aspect", "FATE", "Core", 1, "{}", "{}"));
        await _db.SaveChangesAsync();

        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(2, report.CampaignsMigrated);
        Assert.Equal(1, report.CampaignsBoundToFreeform);
        Assert.Equal(1, report.CharactersMigrated);
        Assert.Equal(1, report.CharactersBoundToFreeform);
    }

    [Fact]
    public async Task MigrateExistingData_ReturnsZeroCounts_WhenNoDataToMigrate()
    {
        // Act
        var report = await _service.MigrateExistingDataAsync();

        // Assert
        Assert.Equal(0, report.CampaignsMigrated);
        Assert.Equal(0, report.CharactersMigrated);
        Assert.Equal(0, report.CampaignsBoundToFreeform);
        Assert.Equal(0, report.CharactersBoundToFreeform);
    }
}

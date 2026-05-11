using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.Integration;

/// <summary>
/// Integration tests for version pinning behavior.
/// Verifies that campaigns pin to a specific definition version and can be migrated
/// to newer versions independently.
/// </summary>
public class VersionPinningIntegrationTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly SystemRegistry _registry;
    private readonly GameSystemDefinitionSerializer _serializer;

    public VersionPinningIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: $"VersionPinning_{Guid.NewGuid()}")
            .Options;

        _db = new AircaneDbContext(options);
        _serializer = new GameSystemDefinitionSerializer();
        var validator = new GameSystemDefinitionValidator();
        _registry = new SystemRegistry(_db, _serializer, validator);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task VersionPinning_CampaignBoundToV1_StillUsesV1AfterV2Update()
    {
        // Step 1: Create definition v1
        var definitionV1 = CreateDefinition("pin-test", "Pin Test System", "1.0.0");
        var definitionId = await _registry.CreateAsync(definitionV1);

        // Get the v1 version record
        var v1Version = await _db.GameSystemDefinitionVersions
            .FirstAsync(v => v.GameSystemDefinitionId == definitionId && v.Version == "1.0.0");

        // Step 2: Create campaign bound to definition, pinned to v1
        var campaign = new Campaign("Pinned Campaign", "Pin Test", "1.0")
        {
            GameSystemDefinitionId = definitionId,
            GameSystemDefinitionVersionId = v1Version.Id
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Step 3: Update definition to v2
        var definitionV2 = CreateDefinition("pin-test", "Pin Test System Updated", "2.0.0");
        await _registry.UpdateAsync(definitionId, definitionV2);

        // Step 4: Verify the definition entity now shows v2
        var currentDefinition = await _registry.GetByIdAsync(definitionId);
        Assert.Equal("Pin Test System Updated", currentDefinition.Name);
        Assert.Equal("2.0.0", currentDefinition.Version);

        // Step 5: Verify campaign is still pinned to v1 version
        var reloadedCampaign = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal(v1Version.Id, reloadedCampaign!.GameSystemDefinitionVersionId);

        // The v1 version record still exists with original data
        var pinnedVersion = await _db.GameSystemDefinitionVersions.FindAsync(v1Version.Id);
        Assert.NotNull(pinnedVersion);
        Assert.Equal("1.0.0", pinnedVersion.Version);
        Assert.Contains("pin-test", pinnedVersion.DefinitionJson);
    }

    [Fact]
    public async Task VersionPinning_MigrateCampaignToV2_CampaignNowUsesV2()
    {
        // Step 1: Create definition v1
        var definitionV1 = CreateDefinition("migrate-test", "Migrate Test", "1.0.0");
        var definitionId = await _registry.CreateAsync(definitionV1);

        var v1Version = await _db.GameSystemDefinitionVersions
            .FirstAsync(v => v.GameSystemDefinitionId == definitionId && v.Version == "1.0.0");

        // Step 2: Create campaign pinned to v1
        var campaign = new Campaign("Migrate Campaign", "Migrate Test", "1.0")
        {
            GameSystemDefinitionId = definitionId,
            GameSystemDefinitionVersionId = v1Version.Id
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Step 3: Update definition to v2
        var definitionV2 = CreateDefinition("migrate-test", "Migrate Test V2", "2.0.0");
        await _registry.UpdateAsync(definitionId, definitionV2);

        var v2Version = await _db.GameSystemDefinitionVersions
            .FirstAsync(v => v.GameSystemDefinitionId == definitionId && v.Version == "2.0.0");

        // Step 4: Migrate campaign to v2 (update the pinned version)
        var reloadedCampaign = await _db.Campaigns.FindAsync(campaign.Id);
        reloadedCampaign!.GameSystemDefinitionVersionId = v2Version.Id;
        await _db.SaveChangesAsync();

        // Step 5: Verify campaign now uses v2
        var finalCampaign = await _db.Campaigns.FindAsync(campaign.Id);
        Assert.Equal(v2Version.Id, finalCampaign!.GameSystemDefinitionVersionId);

        // Verify v2 version has the updated data
        var pinnedVersion = await _db.GameSystemDefinitionVersions.FindAsync(v2Version.Id);
        Assert.Equal("2.0.0", pinnedVersion!.Version);
    }

    [Fact]
    public async Task VersionPinning_MultipleVersionsExist_AllAccessible()
    {
        // Create definition and update it multiple times
        var v1 = CreateDefinition("multi-ver", "Multi Version", "1.0.0");
        var definitionId = await _registry.CreateAsync(v1);

        var v2 = CreateDefinition("multi-ver", "Multi Version V2", "2.0.0");
        await _registry.UpdateAsync(definitionId, v2);

        var v3 = CreateDefinition("multi-ver", "Multi Version V3", "3.0.0");
        await _registry.UpdateAsync(definitionId, v3);

        // Verify all 3 versions exist
        var versions = await _db.GameSystemDefinitionVersions
            .Where(v => v.GameSystemDefinitionId == definitionId)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync();

        Assert.Equal(3, versions.Count);
        Assert.Equal("1.0.0", versions[0].Version);
        Assert.Equal("2.0.0", versions[1].Version);
        Assert.Equal("3.0.0", versions[2].Version);
    }

    [Fact]
    public async Task VersionPinning_CampaignWithoutPin_UsesLatestDefinition()
    {
        // Create definition v1
        var v1 = CreateDefinition("no-pin", "No Pin System", "1.0.0");
        var definitionId = await _registry.CreateAsync(v1);

        // Create campaign without version pin (GameSystemDefinitionVersionId = null)
        var campaign = new Campaign("Unpinned Campaign", "No Pin", "1.0")
        {
            GameSystemDefinitionId = definitionId,
            GameSystemDefinitionVersionId = null // Not pinned
        };
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        // Update definition to v2
        var v2 = CreateDefinition("no-pin", "No Pin System V2", "2.0.0");
        await _registry.UpdateAsync(definitionId, v2);

        // GetByCampaignAsync should return the latest definition
        var loaded = await _registry.GetByCampaignAsync(campaign.Id);
        Assert.Equal("No Pin System V2", loaded.Name);
        Assert.Equal("2.0.0", loaded.Version);
    }

    [Fact]
    public async Task VersionPinning_TwoCampaignsDifferentVersions_EachGetsCorrectVersion()
    {
        // Create definition v1
        var v1 = CreateDefinition("shared-def", "Shared Definition", "1.0.0");
        var definitionId = await _registry.CreateAsync(v1);

        var v1Version = await _db.GameSystemDefinitionVersions
            .FirstAsync(v => v.GameSystemDefinitionId == definitionId && v.Version == "1.0.0");

        // Create campaign 1 pinned to v1
        var campaign1 = new Campaign("Campaign V1", "Shared", "1.0")
        {
            GameSystemDefinitionId = definitionId,
            GameSystemDefinitionVersionId = v1Version.Id
        };
        _db.Campaigns.Add(campaign1);
        await _db.SaveChangesAsync();

        // Update to v2
        var v2 = CreateDefinition("shared-def", "Shared Definition V2", "2.0.0");
        await _registry.UpdateAsync(definitionId, v2);

        var v2Version = await _db.GameSystemDefinitionVersions
            .FirstAsync(v => v.GameSystemDefinitionId == definitionId && v.Version == "2.0.0");

        // Create campaign 2 pinned to v2
        var campaign2 = new Campaign("Campaign V2", "Shared", "2.0")
        {
            GameSystemDefinitionId = definitionId,
            GameSystemDefinitionVersionId = v2Version.Id
        };
        _db.Campaigns.Add(campaign2);
        await _db.SaveChangesAsync();

        // Verify each campaign has its own version pin
        var loaded1 = await _db.Campaigns.FindAsync(campaign1.Id);
        var loaded2 = await _db.Campaigns.FindAsync(campaign2.Id);

        Assert.Equal(v1Version.Id, loaded1!.GameSystemDefinitionVersionId);
        Assert.Equal(v2Version.Id, loaded2!.GameSystemDefinitionVersionId);

        // Verify the version records contain different data
        var ver1 = await _db.GameSystemDefinitionVersions.FindAsync(v1Version.Id);
        var ver2 = await _db.GameSystemDefinitionVersions.FindAsync(v2Version.Id);
        Assert.Contains("1.0.0", ver1!.DefinitionJson);
        Assert.Contains("2.0.0", ver2!.DefinitionJson);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GameSystemDefinition CreateDefinition(string identifier, string name, string version)
    {
        return new GameSystemDefinition(identifier, name, version, 1, "user-created")
        {
            DiceConventions = new List<DiceConvention>
            {
                new()
                {
                    Name = "primary",
                    Type = DiceConventionType.SingleDieModifier,
                    Die = "d20",
                    Description = "Standard d20 roll"
                }
            },
            ResolutionRules = new List<ResolutionRule>
            {
                new()
                {
                    Name = "check",
                    Type = ResolutionRuleType.TargetNumber,
                    Roll = "primary",
                    Comparison = ">=",
                    TargetSource = "dc"
                }
            }
        };
    }
}

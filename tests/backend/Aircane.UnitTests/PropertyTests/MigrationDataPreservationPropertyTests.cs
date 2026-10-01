using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.PropertyTests;

/// <summary>
/// Wrapper type for D&D 5e campaigns to disambiguate FsCheck arbitraries.
/// </summary>
public record DnD5eCampaignWrapper(Campaign Value);

/// <summary>
/// Wrapper type for D&D 5e characters to disambiguate FsCheck arbitraries.
/// </summary>
public record DnD5eCharacterWrapper(Character Value);

/// <summary>
/// Wrapper type for unrecognized game system campaigns.
/// </summary>
public record UnrecognizedCampaignWrapper(Campaign Value);

/// <summary>
/// Wrapper type for unrecognized game system characters.
/// </summary>
public record UnrecognizedCharacterWrapper(Character Value);

/// <summary>
/// Property-based tests for migration data preservation.
/// **Validates: Requirements 12.2, 12.3**
///
/// Property 20: Migration preserves data and binds correctly
/// - For any existing campaign with GameSystem="D&D 5e" and Ruleset="2014", the migration
///   SHALL set GameSystemDefinitionId to the built-in D&D 5e definition ID without modifying
///   any other campaign fields.
/// - For any existing character with matching GameSystem/Ruleset, the migration SHALL preserve
///   all character data (CanonicalJson, CurrentStateJson, Level, Name) unchanged.
/// </summary>
public class MigrationDataPreservationPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────

    private static Gen<string> NonEmptyName =>
        Gen.Elements(
            "Thorin", "Gandalf", "Legolas", "Aragorn", "Frodo",
            "Campaign Alpha", "Dragon's Lair", "The Lost Mine",
            "Shadow Campaign", "Epic Quest", "Dark Dungeon");

    private static Gen<int> ValidLevel =>
        Gen.Choose(1, 20);

    private static Gen<string> ValidCanonicalJson =>
        Gen.Elements(
            "{\"str\":10,\"dex\":14,\"con\":12}",
            "{\"str\":18,\"dex\":8,\"con\":16,\"int\":10,\"wis\":12,\"cha\":14}",
            "{\"class\":\"Fighter\",\"level\":5,\"hp_max\":44}",
            "{\"name\":\"Test\",\"abilities\":{\"str\":15}}",
            "{}",
            "{\"spells\":[\"fireball\",\"shield\"],\"slots\":{\"1\":4,\"2\":3}}");

    private static Gen<string> ValidCurrentStateJson =>
        Gen.Elements(
            "{\"hp\":30,\"conditions\":[]}",
            "{\"hp\":1,\"conditions\":[\"Poisoned\"]}",
            "{\"hp\":52,\"temp_hp\":5,\"conditions\":[\"Invisible\"]}",
            "{}",
            "{\"hp\":0,\"death_saves\":{\"successes\":1,\"failures\":2}}",
            "{\"hp\":100,\"spell_slots\":{\"1\":2,\"2\":1}}");

    private static Gen<AiRole> ValidAiRole =>
        Gen.Elements(AiRole.Assistant, AiRole.CoDm, AiRole.FullDm);

    private static Gen<AiAuthority> ValidAiAuthority =>
        Gen.Elements(AiAuthority.SuggestOnly, AiAuthority.AskBeforeApplying, AiAuthority.FullSessionControl);

    private static Gen<string> UnrecognizedGameSystem =>
        Gen.Elements(
            "Shadowrun", "FATE", "Pathfinder", "Call of Cthulhu",
            "Savage Worlds", "Blades in the Dark", "Dungeon World",
            "Stars Without Number", "Mothership", "Mork Borg");

    private static Gen<string> UnrecognizedRuleset =>
        Gen.Elements("6e", "Core", "2e", "7e", "Deluxe", "1e", "Revised");

    private static Gen<DnD5eCampaignWrapper> DnD5eCampaignGen =>
        from name in NonEmptyName
        from aiRole in ValidAiRole
        from aiAuthority in ValidAiAuthority
        select new DnD5eCampaignWrapper(new Campaign(name, "D&D 5e", "2014", aiRole, aiAuthority));

    private static Gen<DnD5eCharacterWrapper> DnD5eCharacterGen =>
        from name in NonEmptyName
        from level in ValidLevel
        from canonical in ValidCanonicalJson
        from state in ValidCurrentStateJson
        select new DnD5eCharacterWrapper(new Character(name, "D&D 5e", "2014", level, canonical, state));

    private static Gen<UnrecognizedCampaignWrapper> UnrecognizedCampaignGen =>
        from name in NonEmptyName
        from gameSystem in UnrecognizedGameSystem
        from ruleset in UnrecognizedRuleset
        from aiRole in ValidAiRole
        from aiAuthority in ValidAiAuthority
        select new UnrecognizedCampaignWrapper(new Campaign(name, gameSystem, ruleset, aiRole, aiAuthority));

    private static Gen<UnrecognizedCharacterWrapper> UnrecognizedCharacterGen =>
        from name in NonEmptyName
        from gameSystem in UnrecognizedGameSystem
        from ruleset in UnrecognizedRuleset
        from level in ValidLevel
        from canonical in ValidCanonicalJson
        from state in ValidCurrentStateJson
        select new UnrecognizedCharacterWrapper(new Character(name, gameSystem, ruleset, level, canonical, state));

    // ── Arbitraries ───────────────────────────────────────────────────────────

    public static Arbitrary<DnD5eCampaignWrapper> DnD5eCampaignArbitrary => Arb.From(DnD5eCampaignGen);
    public static Arbitrary<DnD5eCharacterWrapper> DnD5eCharacterArbitrary => Arb.From(DnD5eCharacterGen);
    public static Arbitrary<UnrecognizedCampaignWrapper> UnrecognizedCampaignArbitrary => Arb.From(UnrecognizedCampaignGen);
    public static Arbitrary<UnrecognizedCharacterWrapper> UnrecognizedCharacterArbitrary => Arb.From(UnrecognizedCharacterGen);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (AircaneDbContext db, GameSystemMigrationService service) CreateTestContext()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: $"PropTest_{Guid.NewGuid()}")
            .Options;

        var db = new AircaneDbContext(options);

        // Seed built-in definitions
        var dnd5e = DnD5e2014Seed.Create();
        dnd5e.DefinitionJson = "{}";
        db.GameSystemDefinitions.Add(dnd5e);

        var freeform = GenericFreeformSeed.Create();
        freeform.DefinitionJson = "{}";
        db.GameSystemDefinitions.Add(freeform);

        db.SaveChanges();

        var service = new GameSystemMigrationService(db);
        return (db, service);
    }

    // ── Property Tests ────────────────────────────────────────────────────────

    /// <summary>
    /// Property 20a: For any D&D 5e campaign, migration sets GameSystemDefinitionId
    /// to the built-in D&D 5e definition ID.
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(Arbitrary = [typeof(MigrationDataPreservationPropertyTests)], MaxTest = 50)]
    public void DnD5eCampaign_Migration_SetsCorrectDefinitionId(DnD5eCampaignWrapper wrapper)
    {
        var campaign = wrapper.Value;
        var (db, service) = CreateTestContext();
        try
        {
            db.Campaigns.Add(campaign);
            db.SaveChanges();

            service.MigrateExistingDataAsync().GetAwaiter().GetResult();

            var migrated = db.Campaigns.Find(campaign.Id)!;
            Assert.Equal(DnD5e2014Seed.DefinitionId, migrated.GameSystemDefinitionId);
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <summary>
    /// Property 20b: For any D&D 5e campaign, migration preserves all other campaign fields unchanged.
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(Arbitrary = [typeof(MigrationDataPreservationPropertyTests)], MaxTest = 50)]
    public void DnD5eCampaign_Migration_PreservesAllOtherFields(DnD5eCampaignWrapper wrapper)
    {
        var campaign = wrapper.Value;
        var (db, service) = CreateTestContext();
        try
        {
            // Capture original values before migration
            var originalName = campaign.Name;
            var originalGameSystem = campaign.GameSystem;
            var originalRuleset = campaign.Ruleset;
            var originalAiRole = campaign.AiRole;
            var originalAiAuthority = campaign.AiAuthority;
            var originalId = campaign.Id;
            var originalCreatedAt = campaign.CreatedAt;

            db.Campaigns.Add(campaign);
            db.SaveChanges();

            service.MigrateExistingDataAsync().GetAwaiter().GetResult();

            var migrated = db.Campaigns.Find(campaign.Id)!;
            Assert.Equal(originalName, migrated.Name);
            Assert.Equal(originalGameSystem, migrated.GameSystem);
            Assert.Equal(originalRuleset, migrated.Ruleset);
            Assert.Equal(originalAiRole, migrated.AiRole);
            Assert.Equal(originalAiAuthority, migrated.AiAuthority);
            Assert.Equal(originalId, migrated.Id);
            Assert.Equal(originalCreatedAt, migrated.CreatedAt);
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <summary>
    /// Property 20c: For any D&D 5e character, migration preserves all character data
    /// (CanonicalJson, CurrentStateJson, Level, Name) unchanged.
    /// **Validates: Requirements 12.3**
    /// </summary>
    [Property(Arbitrary = [typeof(MigrationDataPreservationPropertyTests)], MaxTest = 50)]
    public void DnD5eCharacter_Migration_PreservesAllCharacterData(DnD5eCharacterWrapper wrapper)
    {
        var character = wrapper.Value;
        var (db, service) = CreateTestContext();
        try
        {
            // Capture original values
            var originalName = character.Name;
            var originalLevel = character.Level;
            var originalCanonicalJson = character.CanonicalJson;
            var originalCurrentStateJson = character.CurrentStateJson;
            var originalGameSystem = character.GameSystem;
            var originalRuleset = character.Ruleset;
            var originalId = character.Id;

            db.Characters.Add(character);
            db.SaveChanges();

            service.MigrateExistingDataAsync().GetAwaiter().GetResult();

            var migrated = db.Characters.Find(character.Id)!;
            Assert.Equal(DnD5e2014Seed.DefinitionId, migrated.GameSystemDefinitionId);
            Assert.Equal(originalName, migrated.Name);
            Assert.Equal(originalLevel, migrated.Level);
            Assert.Equal(originalCanonicalJson, migrated.CanonicalJson);
            Assert.Equal(originalCurrentStateJson, migrated.CurrentStateJson);
            Assert.Equal(originalGameSystem, migrated.GameSystem);
            Assert.Equal(originalRuleset, migrated.Ruleset);
            Assert.Equal(originalId, migrated.Id);
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <summary>
    /// Property 20d: For any campaign with an unrecognized game system, migration binds
    /// it to the Generic Freeform definition without modifying other fields.
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(Arbitrary = [typeof(MigrationDataPreservationPropertyTests)], MaxTest = 50)]
    public void UnrecognizedCampaign_Migration_BindsToFreeformAndPreservesFields(UnrecognizedCampaignWrapper wrapper)
    {
        var campaign = wrapper.Value;
        var (db, service) = CreateTestContext();
        try
        {
            var originalName = campaign.Name;
            var originalGameSystem = campaign.GameSystem;
            var originalRuleset = campaign.Ruleset;
            var originalAiRole = campaign.AiRole;
            var originalAiAuthority = campaign.AiAuthority;

            db.Campaigns.Add(campaign);
            db.SaveChanges();

            service.MigrateExistingDataAsync().GetAwaiter().GetResult();

            var migrated = db.Campaigns.Find(campaign.Id)!;
            Assert.Equal(GenericFreeformSeed.DefinitionId, migrated.GameSystemDefinitionId);
            Assert.Equal(originalName, migrated.Name);
            Assert.Equal(originalGameSystem, migrated.GameSystem);
            Assert.Equal(originalRuleset, migrated.Ruleset);
            Assert.Equal(originalAiRole, migrated.AiRole);
            Assert.Equal(originalAiAuthority, migrated.AiAuthority);
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <summary>
    /// Property 20e: For any character with an unrecognized game system, migration binds
    /// it to the Generic Freeform definition and preserves all character data.
    /// **Validates: Requirements 12.3**
    /// </summary>
    [Property(Arbitrary = [typeof(MigrationDataPreservationPropertyTests)], MaxTest = 50)]
    public void UnrecognizedCharacter_Migration_BindsToFreeformAndPreservesData(UnrecognizedCharacterWrapper wrapper)
    {
        var character = wrapper.Value;
        var (db, service) = CreateTestContext();
        try
        {
            var originalName = character.Name;
            var originalLevel = character.Level;
            var originalCanonicalJson = character.CanonicalJson;
            var originalCurrentStateJson = character.CurrentStateJson;

            db.Characters.Add(character);
            db.SaveChanges();

            service.MigrateExistingDataAsync().GetAwaiter().GetResult();

            var migrated = db.Characters.Find(character.Id)!;
            Assert.Equal(GenericFreeformSeed.DefinitionId, migrated.GameSystemDefinitionId);
            Assert.Equal(originalName, migrated.Name);
            Assert.Equal(originalLevel, migrated.Level);
            Assert.Equal(originalCanonicalJson, migrated.CanonicalJson);
            Assert.Equal(originalCurrentStateJson, migrated.CurrentStateJson);
        }
        finally
        {
            db.Dispose();
        }
    }
}

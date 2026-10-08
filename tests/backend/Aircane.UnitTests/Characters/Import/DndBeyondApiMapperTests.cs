using System.Text.Json;
using Aircane.Application.Characters.Import;
using Xunit;

namespace Aircane.UnitTests.Characters.Import;

public sealed class DndBeyondApiMapperTests
{
    private static JsonDocument LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Characters", "Import", "Fixtures", name);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    [Fact]
    public void CanMap_ReturnsTrue_ForDdbApiExport()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        Assert.True(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_ReturnsFalse_WhenRequiredKeysMissing()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse("""{"id":1,"classes":[],"stats":[]}""");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void CanMap_DoesNotThrow_OnNonObjectRoot()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse("[]");

        Assert.False(mapper.CanMap(doc));
    }

    [Fact]
    public void Map_SumsBaseBonusAndModifierLayers()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        // STR: 15 base + 2 bonusStats + 1 race modifier = 18.
        Assert.Equal(18, result.Character.Abilities.Strength);
        Assert.Equal("18", result.MappedFields[CanonicalCharacterPaths.AbilityStrength]);

        // CON: 13 base + 1 bonusStats = 14.
        Assert.Equal(14, result.Character.Abilities.Constitution);

        // WIS: 12 base + 2 feat modifier = 14.
        Assert.Equal(14, result.Character.Abilities.Wisdom);
    }

    [Fact]
    public void Map_OverrideStat_ReplacesBaseAndBonus()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse(
            """
            {
              "id":1,"dateModified":"x","race":{"fullName":"Human"},
              "stats":[{"id":1,"value":10}],
              "bonusStats":[{"id":1,"value":5}],
              "overrideStats":[{"id":1,"value":19}],
              "classes":[{"level":1,"definition":{"name":"Fighter"}}]
            }
            """);

        var result = mapper.Map(doc);

        // Override (19) wins over base+bonus (15).
        Assert.Equal(19, result.Character.Abilities.Strength);
    }

    [Fact]
    public void Map_OutOfRangeSummedScore_ForcedIntoRequiresReview()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse(
            """
            {
              "id":1,"dateModified":"x","race":{"fullName":"Human"},
              "stats":[{"id":1,"value":28}],
              "bonusStats":[{"id":1,"value":10}],
              "classes":[{"level":1,"definition":{"name":"Fighter"}}]
            }
            """);

        var result = mapper.Map(doc);

        // 28 + 10 = 38, kept but flagged for review.
        Assert.Equal(38, result.Character.Abilities.Strength);
        Assert.Equal("38", result.MappedFields[CanonicalCharacterPaths.AbilityStrength]);
        Assert.Contains(CanonicalCharacterPaths.AbilityStrength, result.RequiresReviewPaths);
    }

    [Fact]
    public void Map_InRangeScore_NotFlaggedForReview()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.DoesNotContain(CanonicalCharacterPaths.AbilityStrength, result.RequiresReviewPaths);
    }

    [Fact]
    public void Map_ComputesHitPointsFromBasePlusConPerLevel()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        // base 34 + (CON mod +2 * level 5) = 44.
        Assert.Equal(44, result.Character.Combat.MaxHitPoints);
        Assert.Equal("44", result.MappedFields[CanonicalCharacterPaths.CombatMaxHitPoints]);

        // current = max - removedHitPoints(4) = 40.
        Assert.Equal(40, result.Character.Combat.CurrentHitPoints);
        Assert.Equal("40", result.MappedFields[CanonicalCharacterPaths.CombatHitPoints]);

        Assert.Equal(5, result.Character.Combat.TemporaryHitPoints);
    }

    [Fact]
    public void Map_ReadsTotalArmorClass()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.Equal(18, result.Character.Combat.ArmorClass);
        Assert.Equal("18", result.MappedFields[CanonicalCharacterPaths.CombatArmorClass]);
    }

    [Fact]
    public void Map_OverrideArmorClass_WinsOverTotal()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse(
            """
            {
              "id":1,"dateModified":"x","race":{"fullName":"Human"},
              "stats":[{"id":1,"value":10}],
              "armorClass":{"totalArmorClass":15},
              "overrideArmorClass":20,
              "classes":[{"level":1,"definition":{"name":"Fighter"}}]
            }
            """);

        var result = mapper.Map(doc);

        Assert.Equal(20, result.Character.Combat.ArmorClass);
    }

    [Fact]
    public void Map_ReadsClassLevelAndSubclass()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.Single(result.Character.Classes);
        Assert.Equal("Fighter", result.Character.Classes[0].ClassName);
        Assert.Equal("Champion", result.Character.Classes[0].Subclass);
        Assert.Equal(5, result.Character.Classes[0].Level);
        Assert.Equal("5", result.MappedFields[CanonicalCharacterPaths.Level]);
    }

    [Fact]
    public void Map_SumsMulticlassLevels()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse(
            """
            {
              "id":1,"dateModified":"x","race":{"fullName":"Human"},
              "stats":[{"id":1,"value":10}],
              "classes":[
                {"level":3,"definition":{"name":"Fighter"}},
                {"level":2,"definition":{"name":"Rogue"}}
              ]
            }
            """);

        var result = mapper.Map(doc);

        Assert.Equal(5, result.Character.Classes[0].Level);
        Assert.Equal("Fighter", result.Character.Classes[0].ClassName);
    }

    [Fact]
    public void Map_PutsSubraceIntoExtraFields()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.Equal("Mountain", result.ExtraFields["Subrace"]);
    }

    [Fact]
    public void Map_SummarizesSpellsAndInventoryIntoExtraFields()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.Contains("Fire Bolt", result.ExtraFields["Spells"]);
        Assert.Contains("Longsword", result.ExtraFields["Inventory"]);
    }

    [Fact]
    public void DetectRuleset_SourceIdInSet_Returns2024()
    {
        using var doc = Parse(
            """
            {"classes":[{"definition":{"sources":[{"sourceId":672}]}}]}
            """);

        Assert.Equal("2024", DndBeyondApiMapper.DetectRuleset(doc.RootElement));
    }

    [Fact]
    public void DetectRuleset_SourceIdNotInSet_Returns2014()
    {
        using var doc = Parse(
            """
            {"classes":[{"definition":{"sources":[{"sourceId":1}]}}]}
            """);

        Assert.Equal("2014", DndBeyondApiMapper.DetectRuleset(doc.RootElement));
    }

    [Fact]
    public void Map_AlwaysRequiresRulesetConfirmation_AndForcesDnd5e2014()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = LoadFixture("ddb-v5.json");

        var result = mapper.Map(doc);

        Assert.Equal("2014", result.Ruleset);
        Assert.True(result.RulesetRequiresConfirmation);
        Assert.Equal("dnd-5e-2014", result.GameSystemIdentifier);
        Assert.Equal(ImportConfidence.High, result.Confidence);
    }

    [Fact]
    public void Map_DoesNotThrow_OnEmptyObject()
    {
        var mapper = new DndBeyondApiMapper();
        using var doc = Parse("{}");

        var result = mapper.Map(doc);

        Assert.Empty(result.MappedFields);
    }
}

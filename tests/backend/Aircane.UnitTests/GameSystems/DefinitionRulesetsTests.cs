using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.GameSystems;

/// <summary>
/// The optional <c>metadata.rulesets</c> list: parsing, printing, validation, the built-in seeds, and the
/// seeder backfill that gives existing installs the built-in labels.
/// </summary>
public sealed class DefinitionRulesetsTests : IDisposable
{
    private readonly GameSystemDefinitionSerializer _serializer = new();
    private readonly GameSystemDefinitionValidator _validator = new();
    private readonly AircaneDbContext _db;

    public DefinitionRulesetsTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private const string OldDefinitionJson = """
        {
          "schemaVersion": 1,
          "metadata": { "id": "old-system", "name": "Old System", "version": "1.0.0", "license": "user-created", "tags": ["d6"] }
        }
        """;

    private GameSystemDefinitionSeeder Seeder() =>
        new(_db, _serializer, _validator, NullLogger<GameSystemDefinitionSeeder>.Instance);

    // ── Parse / print ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ReadsMetadataRulesets()
    {
        var json = OldDefinitionJson.Replace("\"tags\": [\"d6\"]", "\"tags\": [\"d6\"], \"rulesets\": [\"2014\", \"Revised\"]");

        var result = _serializer.ParseJson(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "2014", "Revised" }, result.Definition!.Rulesets);
    }

    [Fact]
    public void Parse_AnOldDefinitionWithoutRulesets_IsValid_AndHasNone()
    {
        var result = _serializer.ParseJson(OldDefinitionJson);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Definition!.Rulesets);
        Assert.Equal(1, result.Definition.SchemaVersion);
        Assert.True(_validator.Validate(result.Definition).IsValid);
    }

    [Fact]
    public void Print_WritesRulesetsInMetadata_AndOmitsAnEmptyList()
    {
        var withRulesets = new GameSystemDefinition("x", "X", "1.0.0", 1, "user-created") { Rulesets = ["Remaster"] };
        var without = new GameSystemDefinition("y", "Y", "1.0.0", 1, "user-created");

        Assert.Contains("\"rulesets\"", _serializer.PrintJson(withRulesets));
        Assert.DoesNotContain("rulesets", _serializer.PrintJson(without));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_AcceptsTenLabelsOfSixtyCharacters()
    {
        var definition = new GameSystemDefinition("x", "X", "1.0.0", 1, "user-created")
        {
            Rulesets = Enumerable.Range(0, 10).Select(i => $"{i}".PadRight(60, 'r')).ToList(),
        };

        Assert.True(_validator.Validate(definition).IsValid);
    }

    [Fact]
    public void Validator_RejectsElevenLabels()
    {
        var definition = new GameSystemDefinition("x", "X", "1.0.0", 1, "user-created")
        {
            Rulesets = Enumerable.Range(0, 11).Select(i => $"r{i}").ToList(),
        };

        var result = _validator.Validate(definition);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("At most 10 ruleset labels"));
    }

    [Theory]
    [InlineData(61)]
    [InlineData(0)]
    public void Validator_RejectsALabelThatIsTooLongOrEmpty(int length)
    {
        var definition = new GameSystemDefinition("x", "X", "1.0.0", 1, "user-created")
        {
            Rulesets = [new string('r', length)],
        };

        Assert.False(_validator.Validate(definition).IsValid);
    }

    // ── Seeds ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Seeds_DeclareTheirRulesetLabels()
    {
        Assert.Equal(new[] { "2014" }, DnD5e2014Seed.Create().Rulesets);
        Assert.Equal(new[] { "Remaster" }, Pathfinder2eRemasterSeed.Create().Rulesets);
        Assert.Empty(GenericFreeformSeed.Create().Rulesets);
    }

    [Fact]
    public void StarterTemplates_DeclareNoRulesets()
    {
        foreach (var template in StarterTemplates.GetAll())
        {
            var parsed = _serializer.ParseJson(template.DefinitionJson);
            Assert.True(parsed.IsSuccess, template.Id);
            Assert.Empty(parsed.Definition!.Rulesets);
        }
    }

    [Fact]
    public async Task Seeder_OnAFreshDatabase_StoresTheLabelsInTheDefinitionJson()
    {
        await Seeder().SeedAsync();

        Assert.Equal(new[] { "2014" }, await StoredRulesetsAsync(DnD5e2014Seed.DefinitionId));
        Assert.Equal(new[] { "Remaster" }, await StoredRulesetsAsync(Pathfinder2eRemasterSeed.DefinitionId));
        Assert.Empty(await StoredRulesetsAsync(GenericFreeformSeed.DefinitionId));
    }

    [Fact]
    public async Task Seeder_OnAnExistingInstall_AddsTheLabelsToABuiltInStoredWithoutThem()
    {
        // A built-in stored before ruleset labels existed: same id, JSON without "rulesets".
        var old = DnD5e2014Seed.Create();
        old.Rulesets = [];
        old.DefinitionJson = _serializer.PrintJson(old);
        _db.GameSystemDefinitions.Add(old);
        await _db.SaveChangesAsync();
        Assert.DoesNotContain("rulesets", old.DefinitionJson);

        await Seeder().SeedAsync();

        Assert.Equal(new[] { "2014" }, await StoredRulesetsAsync(DnD5e2014Seed.DefinitionId));
        // Everything else in the stored definition survives the backfill.
        var stored = _serializer.ParseJson((await StoredAsync(DnD5e2014Seed.DefinitionId)).DefinitionJson).Definition!;
        Assert.Equal(DnD5e2014Seed.Create().DiceConventions.Count, stored.DiceConventions.Count);
        Assert.Equal(DnD5e2014Seed.Create().ConditionSet.Count, stored.ConditionSet.Count);
    }

    [Fact]
    public async Task Seeder_LeavesABuiltInThatAlreadyDeclaresRulesetsAlone_AndIsIdempotent()
    {
        var edited = DnD5e2014Seed.Create();
        edited.Rulesets = ["PHB 2014"];
        edited.DefinitionJson = _serializer.PrintJson(edited);
        _db.GameSystemDefinitions.Add(edited);
        await _db.SaveChangesAsync();

        await Seeder().SeedAsync();
        await Seeder().SeedAsync();

        Assert.Equal(new[] { "PHB 2014" }, await StoredRulesetsAsync(DnD5e2014Seed.DefinitionId));
        Assert.Equal(new[] { "Remaster" }, await StoredRulesetsAsync(Pathfinder2eRemasterSeed.DefinitionId));
    }

    private async Task<GameSystemDefinition> StoredAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        return await _db.GameSystemDefinitions.AsNoTracking().SingleAsync(d => d.Id == id);
    }

    private async Task<IReadOnlyList<string>> StoredRulesetsAsync(Guid id) =>
        _serializer.ParseJson((await StoredAsync(id)).DefinitionJson).Definition!.Rulesets;
}

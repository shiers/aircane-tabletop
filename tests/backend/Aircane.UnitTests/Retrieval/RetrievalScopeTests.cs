using Aircane.Application.DTOs.Retrieval;
using Aircane.Application.GameSystems;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.DocumentProcessing;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.GameSystems.Seeds;
using Aircane.Infrastructure.Persistence;
using Aircane.Infrastructure.Retrieval;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Retrieval;

/// <summary>
/// Rules retrieval scoped by a campaign's game system definition, against the real seeded
/// definitions, aliases and built-in content bundles. Each search mirrors the AI DM: the campaign's
/// scope is resolved first, and the campaign's own strings are only used when it does not resolve.
/// </summary>
public sealed class RetrievalScopeTests : IDisposable
{
    private const string DndName = "Dungeons & Dragons 5th Edition (2014)";
    private const string Pf2eName = "Pathfinder Second Edition (Remaster)";

    private readonly AircaneDbContext _db;
    private readonly GameSystemCanonicalizer _canonicalizer;
    private readonly RetrievalScopeResolver _resolver;
    private readonly RetrievalService _retrieval;

    public RetrievalScopeTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
        _canonicalizer = new GameSystemCanonicalizer(_db);
        _resolver = new RetrievalScopeResolver(_db, _canonicalizer, new GameSystemDefinitionSerializer());
        _retrieval = new RetrievalService(_db, new FakeEmbeddingProvider(), gameSystemCanonicalizer: _canonicalizer);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SeedDefinitionsAsync()
    {
        var seeder = new GameSystemDefinitionSeeder(
            _db, new GameSystemDefinitionSerializer(), new GameSystemDefinitionValidator(),
            NullLogger<GameSystemDefinitionSeeder>.Instance);
        await seeder.SeedAsync();
    }

    private async Task SeedBuiltInContentAsync()
    {
        var seeder = new BuiltInContentSeeder(
            _db, new FakeEmbeddingProvider(), new SlidingWindowTextChunker(),
            NullLogger<BuiltInContentSeeder>.Instance,
            NullLogger<Aircane.Infrastructure.DocumentSources.EmbeddedResourceDocumentSource>.Instance);
        await seeder.SeedAsync();
    }

    private async Task<SourceDocument> AddDocAsync(string gameSystem, string ruleset, string text)
    {
        var doc = new SourceDocument($"{gameSystem} / {ruleset}", "test.pdf", SourceType.Rules, SourceMode.Upload,
            gameSystem, ruleset, "/path/test.pdf");
        _db.SourceDocuments.Add(doc);
        _db.DocumentChunks.Add(new DocumentChunk(doc.Id, 0, text, pageNumber: 1, sectionTitle: null,
            chunkType: "paragraph", visibility: ContentVisibility.Public));
        await _db.SaveChangesAsync();
        return doc;
    }

    private async Task<Guid> AddDefinitionAsync(string identifier, string name, params string[] rulesets)
    {
        var serializer = new GameSystemDefinitionSerializer();
        var definition = new GameSystemDefinition(identifier, name, "1.0.0", 1, "user-created") { Rulesets = rulesets };
        definition.DefinitionJson = serializer.PrintJson(definition);
        _db.GameSystemDefinitions.Add(definition);
        await _db.SaveChangesAsync();
        return definition.Id;
    }

    /// <summary>Searches as the AI DM does for a campaign: its scope when it resolves, else its own strings.</summary>
    private async Task<IReadOnlyList<Guid>> SearchForCampaignAsync(
        string query, string gameSystem, string ruleset, Guid? definitionId = null)
    {
        var scope = await _resolver.ResolveAsync(definitionId, gameSystem);
        var results = await _retrieval.SearchByKeywordAsync(
            new SearchRequest(query, TopK: 500, GameSystem: gameSystem, Ruleset: ruleset, Scope: scope),
            ContentVisibility.DMOnly);
        return results.Select(r => r.SourceDocumentId).Distinct().ToList();
    }

    private async Task<IReadOnlyList<Guid>> BuiltInDocIdsAsync(string gameSystem) =>
        await _db.SourceDocuments.Where(d => d.IsBuiltIn && d.GameSystem == gameSystem).Select(d => d.Id).ToListAsync();

    // ── A campaign created from the selector (Ruleset = the definition's version) ──

    [Fact]
    public async Task SelectorDndCampaign_FindsChunksFiledAs_DnD5e2014_AndTheBuiltInSrd_AndExcludes2024()
    {
        await SeedDefinitionsAsync();
        await SeedBuiltInContentAsync();
        var filed2014 = await AddDocAsync("D&D 5e", "2014", "Grapple rules for the 2014 edition.");
        var filed2024 = await AddDocAsync("D&D 5e", "2024", "Grapple rules for the 2024 edition.");
        var srd = await BuiltInDocIdsAsync("D&D 5e");
        Assert.NotEmpty(srd);

        var found = await SearchForCampaignAsync("grapple", DndName, "1.0.0");

        Assert.Contains(filed2014.Id, found);
        Assert.Contains(srd[0], found);
        Assert.DoesNotContain(filed2024.Id, found);
    }

    [Fact]
    public async Task SelectorDndCampaign_WithoutAScope_FindsNothing_WhichIsTheBugThisFixes()
    {
        await SeedDefinitionsAsync();
        await AddDocAsync("D&D 5e", "2014", "Grapple rules for the 2014 edition.");

        var results = await _retrieval.SearchByKeywordAsync(
            new SearchRequest("grapple", GameSystem: DndName, Ruleset: "1.0.0"), ContentVisibility.DMOnly);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CampaignWithOnlyTheDefinitionId_ResolvesTheSameScope()
    {
        await SeedDefinitionsAsync();
        await SeedBuiltInContentAsync();
        var filed2014 = await AddDocAsync("D&D 5e", "2014", "Grapple rules for the 2014 edition.");
        var filed2024 = await AddDocAsync("D&D 5e", "2024", "Grapple rules for the 2024 edition.");
        var srd = await BuiltInDocIdsAsync("D&D 5e");

        // The strings match nothing: only the bound id can resolve the scope.
        var found = await SearchForCampaignAsync("grapple", "Our Tuesday game", "1.0.0", DnD5e2014Seed.DefinitionId);

        Assert.Contains(filed2014.Id, found);
        Assert.Contains(srd[0], found);
        Assert.DoesNotContain(filed2024.Id, found);
    }

    [Fact]
    public async Task BoundDefinitionId_WinsOverTheGameSystemText()
    {
        await SeedDefinitionsAsync();
        var dnd = await AddDocAsync("D&D 5e", "2014", "Frightened condition, D&D wording.");
        var pf2e = await AddDocAsync("Pathfinder 2e", "Remaster", "Frightened condition, PF2e wording.");

        var found = await SearchForCampaignAsync("frightened", DndName, "1.0.0", Pathfinder2eRemasterSeed.DefinitionId);

        Assert.Contains(pf2e.Id, found);
        Assert.DoesNotContain(dnd.Id, found);
    }

    [Fact]
    public async Task SelectorPf2eCampaign_FindsChunksFiledAs_Pathfinder2eRemaster_AndTheBuiltInBundle()
    {
        await SeedDefinitionsAsync();
        await SeedBuiltInContentAsync();
        var remaster = await AddDocAsync("Pathfinder 2e", "Remaster", "Frightened in the Remaster.");
        var legacy = await AddDocAsync("Pathfinder 2e", "Core 2019", "Frightened in the 2019 core rules.");
        var dnd = await AddDocAsync("D&D 5e", "2014", "Frightened, D&D wording.");
        var builtIn = await BuiltInDocIdsAsync("Pathfinder 2e");
        Assert.NotEmpty(builtIn);

        var found = await SearchForCampaignAsync("frightened", Pf2eName, "1.0.0");

        Assert.Contains(remaster.Id, found);
        Assert.Contains(builtIn[0], found);
        Assert.DoesNotContain(legacy.Id, found);
        Assert.DoesNotContain(dnd.Id, found);
    }

    [Fact]
    public async Task SelectorPf2eCampaign_ThroughTheDefinitionId_Likewise()
    {
        await SeedDefinitionsAsync();
        var remaster = await AddDocAsync("PF2e", "Pathfinder 2e Remaster", "Frightened in the Remaster.");
        var legacy = await AddDocAsync("PF2e", "Core 2019", "Frightened in the 2019 core rules.");

        var found = await SearchForCampaignAsync("frightened", "Pathfinder night", "1.0.0", Pathfinder2eRemasterSeed.DefinitionId);

        Assert.Contains(remaster.Id, found);
        Assert.DoesNotContain(legacy.Id, found);
    }

    // ── Definitions without ruleset labels ────────────────────────────────────

    [Fact]
    public async Task FreeformCampaign_FindsEveryEditionOfItsSystem()
    {
        await SeedDefinitionsAsync();
        var first = await AddDocAsync("Generic Freeform", "First printing", "Scene framing advice.");
        var second = await AddDocAsync("Generic Freeform", "Second printing", "Scene framing advice, revised.");
        var other = await AddDocAsync("D&D 5e", "2014", "Scene framing in D&D.");

        var found = await SearchForCampaignAsync("scene framing", "Generic Freeform", "1.0.0");

        Assert.Contains(first.Id, found);
        Assert.Contains(second.Id, found);
        Assert.DoesNotContain(other.Id, found);
    }

    [Fact]
    public async Task ImportedDefinitionWithoutRulesets_FindsEveryEditionOfItsSystem()
    {
        await SeedDefinitionsAsync();
        await AddDefinitionAsync("shadowrun-6e", "Shadowrun Sixth World");
        var core = await AddDocAsync("Shadowrun Sixth World", "6e", "Matrix hacking rules.");
        var city = await AddDocAsync("shadowrun-6e", "Seattle Edition", "Matrix hacking in Seattle.");

        var found = await SearchForCampaignAsync("matrix", "Shadowrun Sixth World", "2.3.1");

        Assert.Contains(core.Id, found);
        Assert.Contains(city.Id, found);
    }

    [Fact]
    public async Task ImportedDefinitionWithRulesets_IsScopedToThem()
    {
        await AddDefinitionAsync("shadowrun", "Shadowrun", "6e", "Sixth World");
        var sixth = await AddDocAsync("Shadowrun", "Sixth World", "Matrix hacking, sixth.");
        var fifth = await AddDocAsync("Shadowrun", "5e", "Matrix hacking, fifth.");

        var found = await SearchForCampaignAsync("matrix", "Shadowrun", "1.0.0");

        Assert.Contains(sixth.Id, found);
        Assert.DoesNotContain(fifth.Id, found);
    }

    // ── Unresolved campaigns behave exactly as before ─────────────────────────

    [Fact]
    public async Task UnresolvedLegacyCampaign_FiltersByItsOwnStrings_AsBefore()
    {
        await SeedDefinitionsAsync();
        var match = await AddDocAsync("Homebrew Hack", "v2", "Stress track rules.");
        var otherRuleset = await AddDocAsync("Homebrew Hack", "v1", "Stress track rules, old.");
        var blankRuleset = await AddDocAsync("Homebrew Hack", "", "Stress track rules, undated.");
        var otherSystem = await AddDocAsync("Blades in the Dark", "v2", "Stress track rules, Blades.");

        Assert.Null(await _resolver.ResolveAsync(null, "Homebrew Hack"));
        var found = await SearchForCampaignAsync("stress", "Homebrew Hack", "v2");

        Assert.Equal(new[] { match.Id }, found);
        Assert.DoesNotContain(otherRuleset.Id, found);
        Assert.DoesNotContain(blankRuleset.Id, found);
        Assert.DoesNotContain(otherSystem.Id, found);
    }

    [Fact]
    public async Task LegacyDnDCampaign_WithTheOldStrings_ResolvesToTheDefinition_AndFindsTheSameChunks()
    {
        await SeedDefinitionsAsync();
        var filed2014 = await AddDocAsync("D&D 5e", "2014", "Grapple rules for the 2014 edition.");
        var filed2024 = await AddDocAsync("D&D 5e", "2024", "Grapple rules for the 2024 edition.");

        var found = await SearchForCampaignAsync("grapple", "D&D 5e", "2014");

        Assert.Contains(filed2014.Id, found);
        Assert.DoesNotContain(filed2024.Id, found);
    }

    // ── Blank ruleset / blank game system on a document ───────────────────────

    [Fact]
    public async Task UnderADefinitionScope_ABlankRulesetDocumentWithAMatchingSystem_IsIncluded()
    {
        await SeedDefinitionsAsync();
        var blankRuleset = await AddDocAsync("D&D 5e", "", "Grapple rules, edition unspecified.");
        var whitespaceRuleset = await AddDocAsync("D&D 5e", "   ", "Grapple rules, edition unspecified again.");

        var found = await SearchForCampaignAsync("grapple", DndName, "1.0.0");

        Assert.Contains(blankRuleset.Id, found);
        Assert.Contains(whitespaceRuleset.Id, found);
    }

    [Fact]
    public async Task UnderADefinitionScope_ABlankGameSystemDocument_IsExcluded()
    {
        await SeedDefinitionsAsync();
        var blankSystem = await AddDocAsync("", "2014", "Grapple rules from an untagged folder.");
        var blankBoth = await AddDocAsync("", "", "Grapple rules from an untagged folder, no ruleset.");

        var found = await SearchForCampaignAsync("grapple", DndName, "1.0.0");

        Assert.DoesNotContain(blankSystem.Id, found);
        Assert.DoesNotContain(blankBoth.Id, found);
    }

    // ── Case-insensitive game system ──────────────────────────────────────────

    [Fact]
    public async Task GameSystemComparison_IsCaseInsensitive_ForAScope()
    {
        await SeedDefinitionsAsync();
        var shouty = await AddDocAsync("D&D 5E", "2014", "Grapple rules, shouty tag.");
        var lower = await AddDocAsync("dungeons & dragons 5th edition (2014)", "2014", "Grapple rules, lower tag.");

        var found = await SearchForCampaignAsync("grapple", DndName, "1.0.0");

        Assert.Contains(shouty.Id, found);
        Assert.Contains(lower.Id, found);
    }

    [Fact]
    public async Task GameSystemComparison_IsCaseInsensitive_ForTheCampaignsOwnStrings()
    {
        var doc = await AddDocAsync("Homebrew Hack", "v2", "Stress track rules.");

        var results = await _retrieval.SearchByKeywordAsync(
            new SearchRequest("stress", GameSystem: "HOMEBREW hack", Ruleset: "v2"), ContentVisibility.DMOnly);

        Assert.Equal(doc.Id, Assert.Single(results).SourceDocumentId);
    }

    [Fact]
    public async Task AvailableRulesets_MatchTheGameSystemCaseInsensitively()
    {
        await AddDocAsync("Homebrew Hack", "v2", "Stress track rules.");

        var rulesets = await _retrieval.GetAvailableRulesetsAsync("homebrew HACK");

        Assert.Equal(new[] { "v2" }, rulesets);
    }

    // ── The resolver itself ───────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_ForTheSelectorDndCampaign_ReturnsTheDefinitionsSpellingsAndLabel()
    {
        await SeedDefinitionsAsync();

        var scope = await _resolver.ResolveAsync(null, DndName);

        Assert.NotNull(scope);
        Assert.Equal("dnd-5e-2014", scope.DefinitionIdentifier);
        Assert.Contains(DndName, scope.GameSystems);
        Assert.Contains("dnd-5e-2014", scope.GameSystems);
        Assert.Contains("D&D 5e", scope.GameSystems);
        Assert.Equal(new[] { "2014" }, scope.DeclaredRulesets);
        Assert.Contains("d&d 5e 2014", scope.Rulesets);
        Assert.DoesNotContain("2024", scope.Rulesets);
        Assert.Equal("2014", scope.DisplayRuleset);
    }

    [Fact]
    public async Task Resolver_ForADefinitionWithoutRulesets_HasNoRulesetFilter_AndDisplaysItsName()
    {
        await SeedDefinitionsAsync();

        var scope = await _resolver.ResolveAsync(GenericFreeformSeed.DefinitionId, "anything");

        Assert.NotNull(scope);
        Assert.Empty(scope.Rulesets);
        Assert.Equal("Generic Freeform", scope.DisplayRuleset);
    }

    [Fact]
    public async Task Resolver_ForAnInactiveBoundDefinition_StillUsesIt()
    {
        var id = await AddDefinitionAsync("retired", "Retired System", "Old");
        var definition = await _db.GameSystemDefinitions.SingleAsync(d => d.Id == id);
        definition.IsActive = false;
        await _db.SaveChangesAsync();

        Assert.Equal("retired", (await _resolver.ResolveAsync(id, null))?.DefinitionIdentifier);
        Assert.Null(await _resolver.ResolveAsync(null, "Retired System"));
    }

    [Fact]
    public async Task Resolver_ForAnUnknownBoundId_FallsBackToTheGameSystemText()
    {
        await SeedDefinitionsAsync();

        var scope = await _resolver.ResolveAsync(Guid.NewGuid(), "PF2e");

        Assert.Equal("pathfinder-2e-remaster", scope?.DefinitionIdentifier);
    }

    [Fact]
    public async Task Resolver_ForCampaign_ReadsTheCampaignRow()
    {
        await SeedDefinitionsAsync();
        var campaign = new Campaign("Selector campaign", DndName, "1.0.0", AiRole.FullDm, AiAuthority.FullSessionControl);
        _db.Campaigns.Add(campaign);
        await _db.SaveChangesAsync();

        Assert.Equal("dnd-5e-2014", (await _resolver.ResolveForCampaignAsync(campaign.Id))?.DefinitionIdentifier);
        Assert.Null(await _resolver.ResolveForCampaignAsync(Guid.NewGuid()));
    }
}

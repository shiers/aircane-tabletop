using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Aircane.Infrastructure.Retrieval;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.Retrieval;

/// <summary>
/// Unit tests for <see cref="RetrievalService"/> using an in-memory EF Core database.
/// Vector search tests are skipped in this context because pgvector cosine distance
/// requires a real PostgreSQL instance (see integration tests).
/// </summary>
public class RetrievalServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly FakeEmbeddingProvider _embeddingProvider;
    private readonly RetrievalService _service;

    public RetrievalServiceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);
        _embeddingProvider = new FakeEmbeddingProvider();
        _service = new RetrievalService(_db, _embeddingProvider);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<SourceDocument> AddDocumentAsync(
        string title = "Test Document",
        SourceType sourceType = SourceType.Rules,
        string gameSystem = "D&D 5e",
        string ruleset = "2014",
        ContentVisibility visibility = ContentVisibility.Public)
    {
        var doc = new SourceDocument(title, "test.pdf", sourceType, SourceMode.Upload, gameSystem, ruleset, "/path/test.pdf", visibility: visibility);
        _db.SourceDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return doc;
    }

    private async Task<DocumentChunk> AddChunkAsync(
        Guid sourceDocumentId,
        string text,
        string? sectionTitle = null,
        ContentVisibility visibility = ContentVisibility.Public,
        int chunkIndex = 0)
    {
        var chunk = new DocumentChunk(
            sourceDocumentId,
            chunkIndex,
            text,
            pageNumber: 1,
            sectionTitle: sectionTitle,
            chunkType: "paragraph",
            visibility: visibility);
        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();
        return chunk;
    }

    // ── Keyword search: basic matching ────────────────────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_MatchingText_ReturnsChunk()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "The fireball spell deals 8d6 fire damage.");

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Contains("fireball", results[0].Text.ToLower());
    }

    [Fact]
    public async Task SearchByKeywordAsync_MatchingSectionTitle_ReturnsChunk()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Some generic text.", sectionTitle: "Fireball Spell");

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Equal("Fireball Spell", results[0].SectionTitle);
    }

    [Fact]
    public async Task SearchByKeywordAsync_NoMatch_ReturnsEmpty()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "The wizard casts magic missile.");

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByKeywordAsync_EmptyQuery_ReturnsEmpty()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Some text about spells.");

        var request = new SearchRequest("");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByKeywordAsync_WhitespaceQuery_ReturnsEmpty()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Some text about spells.");

        var request = new SearchRequest("   ");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByKeywordAsync_CaseInsensitive_ReturnsChunk()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "The FIREBALL spell is powerful.");

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
    }

    [Fact]
    public async Task SearchByKeywordAsync_NaturalLanguageQuestion_MatchesIndividualTerms()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Actor. Skilled at mimicry and dramatics, you gain the following benefits.", chunkIndex: 0);
        await AddChunkAsync(doc.Id, "Each skill represents a specific aspect of an ability score.", chunkIndex: 1);
        await AddChunkAsync(doc.Id, "The wizard casts magic missile.", chunkIndex: 2);

        var request = new SearchRequest("explain Actor skill");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        // Both term-matching chunks are found; the one containing every term ranks first.
        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].ChunkIndex);
        Assert.Equal(1.0, results[0].Score);
        Assert.Equal(1, results[1].ChunkIndex);
        Assert.Equal(0.5, results[1].Score);
    }

    [Fact]
    public async Task SearchAsync_PartialKeywordMatch_ScoresBelowFullMatch()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Actor feat: skilled at mimicry.", chunkIndex: 0);
        await AddChunkAsync(doc.Id, "Skill proficiency bonus.", chunkIndex: 1);

        var results = await _service.SearchAsync(new SearchRequest("actor skill"), ContentVisibility.Public);

        // Keyword-only score is 0.7 x the fraction of terms matched.
        Assert.Equal(0.7, results.Single(r => r.ChunkIndex == 0).Score!.Value, 6);
        Assert.Equal(0.35, results.Single(r => r.ChunkIndex == 1).Score!.Value, 6);
    }

    [Fact]
    public async Task SearchAsync_ResultsCarrySourceType()
    {
        var doc = await AddDocumentAsync(sourceType: SourceType.Homebrew);
        await AddChunkAsync(doc.Id, "House rule: grapple uses Athletics.");

        var results = await _service.SearchAsync(new SearchRequest("grapple"), ContentVisibility.Public);

        Assert.Equal(SourceType.Homebrew, Assert.Single(results).SourceType);
    }

    // ── Ruleset filter normalization ──────────────────────────────────────────

    [Theory]
    [InlineData("2014", "2014", true)]
    [InlineData("  2014  ", "2014", true)]
    [InlineData("D&D 5e 2014", "2014", true)]
    [InlineData("dnd 5e 2014", "2014", true)]
    [InlineData("2014", "D&D 5e 2014", true)]
    [InlineData("One DnD", "2024", true)]
    [InlineData("PHB", "phb", true)]
    [InlineData("Remaster", "Pathfinder 2e Remaster", true)]
    [InlineData("2014", "2024", false)]
    [InlineData("PHB", "DMG", false)]
    public async Task SearchByKeywordAsync_RulesetFilter_IsNormalizedAndAliasAware(
        string requested, string stored, bool expectMatch)
    {
        var doc = await AddDocumentAsync(ruleset: stored);
        await AddChunkAsync(doc.Id, "Grapple rules text.");

        var results = await _service.SearchByKeywordAsync(
            new SearchRequest("grapple", Ruleset: requested), ContentVisibility.Public);

        Assert.Equal(expectMatch, results.Count == 1);
    }

    [Fact]
    public async Task SearchByKeywordAsync_RulesetFilter_DifferentRulesetExcluded()
    {
        var doc = await AddDocumentAsync(ruleset: "2024");
        await AddChunkAsync(doc.Id, "Grapple rules text.");

        var results = await _service.SearchByKeywordAsync(
            new SearchRequest("grapple", Ruleset: "2014"), ContentVisibility.Public);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetAvailableRulesetsAsync_ReturnsDistinctRulesets_ForGameSystem()
    {
        await AddDocumentAsync(gameSystem: "D&D 5e", ruleset: "2014");
        await AddDocumentAsync(gameSystem: "D&D 5e", ruleset: "2014");
        await AddDocumentAsync(gameSystem: "D&D 5e", ruleset: "2024");
        await AddDocumentAsync(gameSystem: "Pathfinder 2e", ruleset: "Remaster");
        await AddDocumentAsync(gameSystem: "D&D 5e", ruleset: "");

        var dnd = await _service.GetAvailableRulesetsAsync("D&D 5e");
        var all = await _service.GetAvailableRulesetsAsync(null);

        Assert.Equal(["2014", "2024"], dnd);
        Assert.Equal(["2014", "2024", "Remaster"], all);
    }

    // ── Keyword search: SourceType filter ─────────────────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_FilterBySourceType_OnlyReturnsMatchingType()
    {
        var rulesDoc = await AddDocumentAsync("Rules Doc", SourceType.Rules);
        var adventureDoc = await AddDocumentAsync("Adventure Doc", SourceType.Adventure);

        await AddChunkAsync(rulesDoc.Id, "Fireball rules text.");
        await AddChunkAsync(adventureDoc.Id, "Fireball adventure text.");

        var request = new SearchRequest("fireball", SourceType: SourceType.Rules);
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Equal(rulesDoc.Id, results[0].SourceDocumentId);
    }

    [Fact]
    public async Task SearchByKeywordAsync_FilterBySourceType_NoMatch_ReturnsEmpty()
    {
        var rulesDoc = await AddDocumentAsync("Rules Doc", SourceType.Rules);
        await AddChunkAsync(rulesDoc.Id, "Fireball rules text.");

        var request = new SearchRequest("fireball", SourceType: SourceType.Adventure);
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    // ── Keyword search: GameSystem and Ruleset filters ────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_FilterByGameSystem_OnlyReturnsMatchingSystem()
    {
        var dndDoc = await AddDocumentAsync("D&D Doc", gameSystem: "D&D 5e");
        var pfDoc = await AddDocumentAsync("PF Doc", gameSystem: "Pathfinder 2e");

        await AddChunkAsync(dndDoc.Id, "Fireball D&D text.");
        await AddChunkAsync(pfDoc.Id, "Fireball Pathfinder text.");

        var request = new SearchRequest("fireball", GameSystem: "D&D 5e");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Equal(dndDoc.Id, results[0].SourceDocumentId);
    }

    [Theory]
    [InlineData("Dungeons & Dragons 5th Edition (2014)")]
    [InlineData("dnd-5e-2014")]
    [InlineData("D&D 5e")]
    public async Task SearchByKeywordAsync_FilterByGameSystem_MatchesAnySpellingOfDefinition(string filter)
    {
        // Documents imported through the library are stored under the canonical definition name,
        // while older/built-in documents may carry an alias. Either spelling must match a filter
        // naming the same definition.
        var definition = new GameSystemDefinition("dnd-5e-2014", "Dungeons & Dragons 5th Edition (2014)", "1.0.0", 1, "builtin");
        _db.GameSystemDefinitions.Add(definition);
        _db.GameSystemAliases.Add(new GameSystemAlias(definition.Id, "D&D 5e"));
        await _db.SaveChangesAsync();

        var service = new RetrievalService(
            _db, _embeddingProvider, gameSystemCanonicalizer: new GameSystemCanonicalizer(_db));

        var phb = await AddDocumentAsync("PHB", gameSystem: "Dungeons & Dragons 5th Edition (2014)");
        var srd = await AddDocumentAsync("SRD", gameSystem: "D&D 5e");
        var pf = await AddDocumentAsync("PF Doc", gameSystem: "Pathfinder 2e");
        await AddChunkAsync(phb.Id, "Actor feat text.");
        await AddChunkAsync(srd.Id, "Actor SRD text.");
        await AddChunkAsync(pf.Id, "Actor Pathfinder text.");

        var request = new SearchRequest("actor", GameSystem: filter);
        var results = await service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Equal(
            new[] { phb.Id, srd.Id }.OrderBy(id => id),
            results.Select(r => r.SourceDocumentId).OrderBy(id => id));
    }

    [Fact]
    public async Task SearchByKeywordAsync_FilterByRuleset_OnlyReturnsMatchingRuleset()
    {
        var doc2014 = await AddDocumentAsync("2014 Doc", ruleset: "2014");
        var doc2024 = await AddDocumentAsync("2024 Doc", ruleset: "2024");

        await AddChunkAsync(doc2014.Id, "Fireball 2014 rules.");
        await AddChunkAsync(doc2024.Id, "Fireball 2024 rules.");

        var request = new SearchRequest("fireball", Ruleset: "2014");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Equal(doc2014.Id, results[0].SourceDocumentId);
    }

    // ── Visibility filtering: Player (Public only) ────────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_PlayerRole_CannotSeeDMOnlyChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball public text.", visibility: ContentVisibility.Public);
        await AddChunkAsync(doc.Id, "Fireball DM-only text.", visibility: ContentVisibility.DMOnly, chunkIndex: 1);

        // Player sees only Public
        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Single(results);
        Assert.Equal(ContentVisibility.Public, results[0].Visibility);
    }

    [Fact]
    public async Task SearchByKeywordAsync_PlayerRole_CanSeeRevealedChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball revealed text.", visibility: ContentVisibility.Revealed);

        // Player with Revealed max visibility sees Public + Revealed
        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Revealed);

        Assert.Single(results);
        Assert.Equal(ContentVisibility.Revealed, results[0].Visibility);
    }

    [Fact]
    public async Task SearchByKeywordAsync_PlayerRole_CannotSeeHiddenChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball hidden text.", visibility: ContentVisibility.Hidden);

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    // ── Visibility filtering: DM (Public + Revealed + DMOnly) ─────────────────

    [Fact]
    public async Task SearchByKeywordAsync_DMRole_CanSeeDMOnlyChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball DM-only text.", visibility: ContentVisibility.DMOnly);

        // DM sees Public, Revealed, and DMOnly
        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.DMOnly);

        Assert.Single(results);
        Assert.Equal(ContentVisibility.DMOnly, results[0].Visibility);
    }

    [Fact]
    public async Task SearchByKeywordAsync_DMRole_CannotSeeHiddenChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball hidden text.", visibility: ContentVisibility.Hidden);

        // DM max visibility is DMOnly - Hidden is excluded
        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.DMOnly);

        Assert.Empty(results);
    }

    // ── Visibility filtering: Host (all including Hidden) ─────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_HostRole_CanSeeHiddenChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball hidden text.", visibility: ContentVisibility.Hidden);

        // Host sees everything
        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Hidden);

        Assert.Single(results);
        Assert.Equal(ContentVisibility.Hidden, results[0].Visibility);
    }

    [Fact]
    public async Task SearchByKeywordAsync_HostRole_SeesAllVisibilityLevels()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball public.", visibility: ContentVisibility.Public, chunkIndex: 0);
        await AddChunkAsync(doc.Id, "Fireball revealed.", visibility: ContentVisibility.Revealed, chunkIndex: 1);
        await AddChunkAsync(doc.Id, "Fireball dm-only.", visibility: ContentVisibility.DMOnly, chunkIndex: 2);
        await AddChunkAsync(doc.Id, "Fireball hidden.", visibility: ContentVisibility.Hidden, chunkIndex: 3);

        var request = new SearchRequest("fireball", TopK: 10);
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Hidden);

        Assert.Equal(4, results.Count);
    }

    // ── Result shape ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_Result_ContainsExpectedFields()
    {
        var doc = await AddDocumentAsync("My Rules Book");
        await AddChunkAsync(doc.Id, "Fireball deals fire damage.", sectionTitle: "Spells", chunkIndex: 3);

        var request = new SearchRequest("fireball");
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        var result = Assert.Single(results);
        Assert.Equal(doc.Id, result.SourceDocumentId);
        Assert.Equal("My Rules Book", result.SourceDocumentTitle);
        Assert.Equal("Fireball deals fire damage.", result.Text);
        Assert.Equal("Spells", result.SectionTitle);
        Assert.Equal(3, result.ChunkIndex);
    }

    // ── TopK limit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchByKeywordAsync_RespectsTopKLimit()
    {
        var doc = await AddDocumentAsync();
        for (int i = 0; i < 5; i++)
            await AddChunkAsync(doc.Id, $"Fireball chunk {i}.", chunkIndex: i);

        var request = new SearchRequest("fireball", TopK: 3);
        var results = await _service.SearchByKeywordAsync(request, ContentVisibility.Public);

        Assert.Equal(3, results.Count);
    }

    // ── Combined search ───────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Some text about spells.");

        var request = new SearchRequest("");
        var results = await _service.SearchAsync(request, ContentVisibility.Public);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_MatchingText_ReturnsResults()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "The fireball spell deals 8d6 fire damage.");

        var request = new SearchRequest("fireball");
        var results = await _service.SearchAsync(request, ContentVisibility.Public);

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Text.Contains("fireball", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchAsync_DeduplicatesResultsByChunkId()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball spell description.", chunkIndex: 0);

        var request = new SearchRequest("fireball");
        var results = await _service.SearchAsync(request, ContentVisibility.Public);

        // Even if keyword and vector both find the same chunk, it should appear once
        var distinctIds = results.Select(r => r.ChunkId).Distinct().Count();
        Assert.Equal(results.Count, distinctIds);
    }

    [Fact]
    public async Task SearchAsync_VisibilityFiltering_PlayerCannotSeeDMOnlyChunks()
    {
        var doc = await AddDocumentAsync();
        await AddChunkAsync(doc.Id, "Fireball public.", visibility: ContentVisibility.Public, chunkIndex: 0);
        await AddChunkAsync(doc.Id, "Fireball dm-only.", visibility: ContentVisibility.DMOnly, chunkIndex: 1);

        var request = new SearchRequest("fireball");
        var results = await _service.SearchAsync(request, ContentVisibility.Public);

        Assert.All(results, r => Assert.Equal(ContentVisibility.Public, r.Visibility));
    }
}

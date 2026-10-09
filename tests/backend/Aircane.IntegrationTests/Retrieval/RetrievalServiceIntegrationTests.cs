using Aircane.Application.DTOs.Retrieval;
using Aircane.Domain.Entities;
using Aircane.Domain.Entities.GameSystems;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Persistence;
using Aircane.Infrastructure.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector;
using Xunit;

namespace Aircane.IntegrationTests.Retrieval;

/// <summary>
/// Runs <see cref="RetrievalService"/> and <see cref="RagContextBuilder"/> against a real PostgreSQL
/// with pgvector, covering what the EF InMemory unit tests cannot: the SQL translation of the
/// keyword and filter expressions and the pgvector cosine-distance queries.
/// </summary>
/// <remarks>
/// Each test gets its own database cloned from a migrated template and seeded with the dataset below.
/// With only a handful of rows the planner will normally choose a sequential scan, so these tests
/// verify query correctness and result ordering, not use of the HNSW index.
/// <code>
/// Core Rules   (Rules,    "D&amp;D 5e", ruleset 2014)  c1 Public embedded | c2 Public embedded | c3 DMOnly embedded | c4 Public no vector
/// House Rules  (Homebrew, "D&amp;D 5e", ruleset 2014)  h1 Public embedded | h2 Public no vector
/// PF Rules     (Rules,    "Pathfinder 2e", Remaster) p1 Public no vector
/// Retired      (Rules,    "D&amp;D 5e", DISABLED)      d1 Public embedded
/// </code>
/// </remarks>
public class RetrievalServiceIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string DndDefinitionName = "Dungeons & Dragons 5th Edition (2014)";

    private readonly PostgresFixture _fixture;
    private readonly FakeEmbeddingProvider _embeddings = new();

    private AircaneDbContext _db = null!;
    private RetrievalService _service = null!;

    private DocumentChunk _c1 = null!, _c2 = null!, _c3 = null!, _c4 = null!;
    private DocumentChunk _h1 = null!, _h2 = null!, _p1 = null!, _d1 = null!;

    public RetrievalServiceIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    // ── Setup ─────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        _db = PostgresFixture.CreateContext(connectionString);
        _service = new RetrievalService(
            _db, _embeddings, embeddingCompatibility: null, logger: null,
            gameSystemCanonicalizer: new GameSystemCanonicalizer(_db));

        // Game system definition with an alias, so documents stored as "D&D 5e" are reachable by
        // the definition's full name (the root cause of the bug fixed in 20c1536).
        var definition = new GameSystemDefinition("dnd-5e-2014", DndDefinitionName, "1.0.0", 1, "built-in");
        _db.GameSystemDefinitions.Add(definition);
        _db.GameSystemAliases.Add(new GameSystemAlias(definition.Id, "D&D 5e"));
        await _db.SaveChangesAsync();

        var core = await AddDocumentAsync("Core Rules", SourceType.Rules, "D&D 5e", "2014");
        var house = await AddDocumentAsync("House Rules", SourceType.Homebrew, "D&D 5e", "2014");
        var pathfinder = await AddDocumentAsync("PF Rules", SourceType.Rules, "Pathfinder 2e", "Remaster");
        var retired = await AddDocumentAsync("Retired Rules", SourceType.Rules, "D&D 5e", "2014", disabled: true);

        _c1 = await AddChunkAsync(core, 0, "Grappling: a grappled creature has speed 0. Grapple uses an Athletics check.", embed: true);
        _c2 = await AddChunkAsync(core, 1, "Opportunity attacks: a hostile foe that leaves your reach provokes a free strike.", embed: true);
        _c3 = await AddChunkAsync(core, 2, "Secret trap: the vault floor hides a pit behind the throne. Grapple checks are impossible there.",
            ContentVisibility.DMOnly, embed: true);
        _c4 = await AddChunkAsync(core, 3, "Spell slots: a wizard regains expended spell slots after a long rest.");

        _h1 = await AddChunkAsync(house, 0, "House rule: grapple costs a bonus action instead of an action.", embed: true);
        _h2 = await AddChunkAsync(house, 1, "House rule: critical hits double the dice and add the maximum damage.");

        _p1 = await AddChunkAsync(pathfinder, 0, "Grapple: Pathfinder uses a Fortitude DC for the target.");

        _d1 = await AddChunkAsync(retired, 0, "Grapple rules from the retired edition of the game.", embed: true);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<SourceDocument> AddDocumentAsync(
        string title, SourceType type, string gameSystem, string ruleset, bool disabled = false)
    {
        var document = new SourceDocument(
            title, $"{title}.pdf", type, SourceMode.Upload, gameSystem, ruleset, $"/docs/{title}.pdf",
            visibility: ContentVisibility.Public, importStatus: ImportStatus.Completed)
        {
            IsDisabled = disabled,
        };
        _db.SourceDocuments.Add(document);
        await _db.SaveChangesAsync();
        return document;
    }

    private async Task<DocumentChunk> AddChunkAsync(
        SourceDocument document, int index, string text,
        ContentVisibility visibility = ContentVisibility.Public, bool embed = false)
    {
        var chunk = new DocumentChunk(document.Id, index, text, pageNumber: 1,
            chunkType: "paragraph", visibility: visibility);

        if (embed)
            await SetEmbeddingAsync(chunk, text);

        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();
        return chunk;
    }

    private async Task SetEmbeddingAsync(DocumentChunk chunk, string embeddedText)
    {
        chunk.Embedding = new Vector(await _embeddings.GenerateEmbeddingAsync(embeddedText));
        chunk.EmbeddingProvider = _embeddings.ProviderName;
        chunk.EmbeddingModel = _embeddings.ModelName;
        chunk.EmbeddingDimensions = _embeddings.Dimensions;
    }

    private RagContextBuilder NewContextBuilder() =>
        new(_service, NullLogger<RagContextBuilder>.Instance);

    private const ContentVisibility Host = ContentVisibility.DMOnly;

    // ── Keyword search ────────────────────────────────────────────────────────

    [Fact]
    public async Task KeywordSearch_SingleTerm_ReturnsChunksContainingTerm()
    {
        var results = await _service.SearchByKeywordAsync(new SearchRequest("wizard"), Host);

        Assert.Equal(_c4.Id, Assert.Single(results).ChunkId);
    }

    [Fact]
    public async Task KeywordSearch_MultiTerm_RanksMoreMatchesHigher()
    {
        var results = await _service.SearchByKeywordAsync(new SearchRequest("grapple athletics", TopK: 20), Host);

        // c1 contains both terms; every other match contains only "grapple".
        Assert.Equal(_c1.Id, results[0].ChunkId);
        Assert.Equal(1.0, results[0].Score);
        Assert.True(results.Count > 1);
        Assert.All(results.Skip(1), r => Assert.Equal(0.5, r.Score));
    }

    [Fact]
    public async Task KeywordSearch_CaseInsensitive_ReturnsMatch()
    {
        var results = await _service.SearchByKeywordAsync(new SearchRequest("WIZARD"), Host);

        Assert.Equal(_c4.Id, Assert.Single(results).ChunkId);
    }

    [Fact]
    public async Task KeywordSearch_TermNotPresent_ReturnsEmpty()
    {
        var results = await _service.SearchByKeywordAsync(new SearchRequest("zeppelin"), Host);

        Assert.Empty(results);
    }

    // ── Vector search ─────────────────────────────────────────────────────────

    [Fact]
    public async Task VectorSearch_WithCompatibleEmbeddings_ReturnsRankedResults()
    {
        // The fake provider is deterministic, so querying with c2's exact text yields c2's vector.
        var results = await _service.SearchByVectorAsync(new SearchRequest(_c2.Text), Host);

        // Only embedded chunks of enabled documents: c1, c2, c3, h1.
        Assert.Equal(4, results.Count);
        Assert.Equal(_c2.Id, results[0].ChunkId);
        Assert.True(results[0].Score > 0.999, $"Expected ~1.0 similarity, got {results[0].Score}.");
        Assert.Equal(results.OrderByDescending(r => r.Score).Select(r => r.ChunkId), results.Select(r => r.ChunkId));
        Assert.DoesNotContain(results, r => r.ChunkId == _c4.Id || r.ChunkId == _h2.Id || r.ChunkId == _p1.Id);
    }

    [Fact]
    public async Task VectorSearch_MismatchedProvenance_ExcludesMismatchedChunks()
    {
        _c1.EmbeddingProvider = "Ollama";
        _c1.EmbeddingModel = "nomic-embed-text";
        _h1.EmbeddingProvider = null; // predates provenance tracking: treated as compatible
        _h1.EmbeddingModel = null;
        _h1.EmbeddingDimensions = null;
        await _db.SaveChangesAsync();

        var results = await _service.SearchByVectorAsync(new SearchRequest(_c2.Text), Host);

        Assert.DoesNotContain(results, r => r.ChunkId == _c1.Id);
        Assert.Contains(results, r => r.ChunkId == _h1.Id);
        Assert.Contains(results, r => r.ChunkId == _c2.Id);
    }

    [Fact]
    public async Task VectorSearch_NoVectors_ReturnsEmpty()
    {
        foreach (var chunk in new[] { _c1, _c2, _c3, _h1, _d1 })
            chunk.Embedding = null;
        await _db.SaveChangesAsync();

        var results = await _service.SearchByVectorAsync(new SearchRequest("grapple"), Host);

        Assert.Empty(results);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_VisibilityFilter_ExcludesDmOnlyFromPlayerRole()
    {
        var playerVisibility = RagContextBuilder.GetMaxVisibility(ParticipantRole.Player);

        var results = await _service.SearchAsync(new SearchRequest("grapple", TopK: 20), playerVisibility);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, r => r.ChunkId == _c3.Id);
        Assert.All(results, r => Assert.Equal(ContentVisibility.Public, r.Visibility));
    }

    [Fact]
    public async Task Search_VisibilityFilter_IncludesDmOnlyForHostRole()
    {
        var hostVisibility = RagContextBuilder.GetMaxVisibility(ParticipantRole.Host);

        var keyword = await _service.SearchAsync(new SearchRequest("grapple", TopK: 20), hostVisibility);
        var vector = await _service.SearchByVectorAsync(new SearchRequest(_c3.Text), hostVisibility);

        Assert.Contains(keyword, r => r.ChunkId == _c3.Id);
        Assert.Equal(_c3.Id, vector[0].ChunkId);
    }

    [Fact]
    public async Task Search_DisabledDocument_ExcludedFromResults()
    {
        var combined = await _service.SearchAsync(new SearchRequest("grapple", TopK: 20), Host);
        // Querying with the disabled chunk's exact text would make it the top vector hit if it were not filtered.
        var byVector = await _service.SearchByVectorAsync(new SearchRequest(_d1.Text), Host);

        Assert.DoesNotContain(combined, r => r.ChunkId == _d1.Id);
        Assert.DoesNotContain(byVector, r => r.ChunkId == _d1.Id);
    }

    [Fact]
    public async Task Search_SourceTypeFilter_ReturnsOnlyMatchingType()
    {
        var results = await _service.SearchAsync(
            new SearchRequest("grapple", TopK: 20, SourceType: SourceType.Homebrew), Host);

        Assert.Equal(_h1.Id, Assert.Single(results).ChunkId);
        Assert.All(results, r => Assert.Equal(SourceType.Homebrew, r.SourceType));
    }

    [Fact]
    public async Task Search_GameSystemFilter_AliasExpansion_FindsDocument()
    {
        // Documents are stored as "D&D 5e" (an alias); the filter uses the definition's full name,
        // and also a differently-cased / spaced alias.
        foreach (var gameSystem in new[] { DndDefinitionName, "d&d  5E", "dnd-5e-2014" })
        {
            var results = await _service.SearchAsync(
                new SearchRequest("grapple", TopK: 20, GameSystem: gameSystem), Host);

            Assert.Contains(results, r => r.ChunkId == _c1.Id);
            Assert.Contains(results, r => r.ChunkId == _h1.Id);
            Assert.DoesNotContain(results, r => r.ChunkId == _p1.Id); // Pathfinder document
        }
    }

    [Fact]
    public async Task Search_RulesetFilter_NormalizesAndExpandsAliases()
    {
        // Documents store "2014"; the filter says "  D&D 5e 2014 ".
        var dnd = await _service.SearchAsync(
            new SearchRequest("grapple", TopK: 20, Ruleset: "  D&D 5e 2014 "), Host);
        var remaster = await _service.SearchAsync(
            new SearchRequest("grapple", TopK: 20, Ruleset: "PF2e Remaster"), Host);

        Assert.Contains(dnd, r => r.ChunkId == _c1.Id);
        Assert.DoesNotContain(dnd, r => r.ChunkId == _p1.Id);
        Assert.Equal(_p1.Id, Assert.Single(remaster).ChunkId);
    }

    [Fact]
    public async Task GetAvailableRulesetsAsync_ReturnsDistinctRulesets()
    {
        var all = await _service.GetAvailableRulesetsAsync(null);
        var dnd = await _service.GetAvailableRulesetsAsync(DndDefinitionName);

        Assert.Equal(["2014", "Remaster"], all);
        Assert.Equal(["2014"], dnd);
    }

    // ── Merge and scoring ─────────────────────────────────────────────────────

    [Fact]
    public async Task Search_ChunkFoundByBoth_ScoresHigherThanKeywordOnly()
    {
        // c1's own text: c1 matches every keyword AND is its own nearest vector. p1 has no
        // embedding, so it can only be a keyword match.
        var results = await _service.SearchAsync(new SearchRequest(_c1.Text, TopK: 20), Host);

        var both = results.Single(r => r.ChunkId == _c1.Id);
        var keywordOnly = results.Single(r => r.ChunkId == _p1.Id);

        Assert.Equal(1.0, both.Score); // min(1, 0.7 * 1.0 + 0.5 * ~1.0)
        Assert.True(keywordOnly.Score > 0 && keywordOnly.Score <= 0.7);
        Assert.True(both.Score > keywordOnly.Score);
    }

    [Fact]
    public async Task Search_ChunkFoundByBoth_ScoresHigherThanVectorOnly()
    {
        var results = await _service.SearchAsync(new SearchRequest(_c1.Text, TopK: 20), Host);
        var vectorHits = await _service.SearchByVectorAsync(new SearchRequest(_c1.Text, TopK: 20), Host);

        // c2 shares no keyword with c1's text, so it is found by vector search only.
        var both = results.Single(r => r.ChunkId == _c1.Id);
        var vectorOnly = results.Single(r => r.ChunkId == _c2.Id);
        var rawSimilarity = vectorHits.Single(r => r.ChunkId == _c2.Id).Score!.Value;

        Assert.Equal(0.6 * rawSimilarity, vectorOnly.Score!.Value, 6);
        Assert.True(both.Score > vectorOnly.Score);
    }

    [Fact]
    public async Task Search_MinRelevanceScore_FiltersLowScoringChunks()
    {
        var builder = NewContextBuilder();
        var request = new RagContextRequest(
            Query: _c1.Text, RequesterRole: ParticipantRole.Host, TopK: 20, MinRelevanceScore: 0.4);

        var context = await builder.BuildContextAsync(request);

        // c1 scores 1.0; every other chunk is a weak partial keyword match or a vector near-zero.
        Assert.True(context.TotalChunksRetrieved > 1);
        Assert.Equal(_c1.Id, Assert.Single(context.Citations).ChunkId);
    }

    [Fact]
    public async Task Search_TopK_LimitsResultCount()
    {
        var limited = await _service.SearchAsync(new SearchRequest("grapple", TopK: 2), Host);
        var unlimited = await _service.SearchAsync(new SearchRequest("grapple", TopK: 20), Host);

        Assert.Equal(2, limited.Count);
        Assert.True(unlimited.Count > 2);
    }

    // ── RagContextBuilder against real data ───────────────────────────────────

    [Fact]
    public async Task RagContextBuilder_DeduplicatesOverlappingChunks()
    {
        const string query = "xylophone marimba";
        var document = await AddDocumentAsync("Dup Doc", SourceType.Rules, "Dup System", "1");

        var original = "The xylophone and the marimba are tuned percussion instruments that the bard uses " +
                       "during every performance at the old tavern, where travellers gather each evening to " +
                       "hear songs about distant kingdoms and forgotten heroes of the northern road.";
        var nearCopy = original.Replace("northern road", "southern coast");

        // The original is also embedded as the query, so it is found by keyword AND vector search
        // (higher score); the near-copy is keyword-only.
        var kept = await AddChunkAsync(document, 0, original);
        await SetEmbeddingAsync(kept, query);
        var dropped = await AddChunkAsync(document, 1, nearCopy);
        await _db.SaveChangesAsync();

        var context = await NewContextBuilder().BuildContextAsync(new RagContextRequest(
            Query: query, RequesterRole: ParticipantRole.Host, TopK: 20, MinRelevanceScore: 0.4));

        Assert.True(context.TotalChunksRetrieved >= 2);
        Assert.Equal(kept.Id, Assert.Single(context.Citations).ChunkId);
        Assert.DoesNotContain("southern coast", context.ContextText);
        Assert.DoesNotContain(context.Citations, c => c.ChunkId == dropped.Id);
    }

    [Fact]
    public async Task RagContextBuilder_HomebrewRanksAboveRules_WhenBothMatch()
    {
        // c1 (Rules) matches both terms and scores 0.7+; h1 (Homebrew) matches one and scores ~0.35.
        var context = await NewContextBuilder().BuildContextAsync(new RagContextRequest(
            Query: "grapple athletics", RequesterRole: ParticipantRole.Player, TopK: 20, MinRelevanceScore: 0.1));

        Assert.Equal(_h1.Id, context.Citations[0].ChunkId);
        Assert.Contains(context.Citations, c => c.ChunkId == _c1.Id);
        Assert.True(
            context.ContextText.IndexOf("House rule: grapple", StringComparison.Ordinal)
            < context.ContextText.IndexOf("Grappling: a grappled", StringComparison.Ordinal),
            "The Homebrew chunk should appear before the Rules chunk in the assembled context.");
    }
}

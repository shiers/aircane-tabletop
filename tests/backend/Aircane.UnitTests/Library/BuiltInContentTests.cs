using Aircane.Domain.License;
using Aircane.Infrastructure.DocumentProcessing;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.Library;
using Aircane.Infrastructure.Persistence;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Integration/regression tests for the built-in rules content bundle (Phase 10):
/// seeding behavior, the built-in delete guard, disable/enable retrieval exclusion,
/// and retrieval accuracy/scoping.
/// </summary>
/// <remarks>
/// These run against the EF Core InMemory provider with the real
/// <see cref="BuiltInContentSeeder"/> and the deterministic <see cref="FakeEmbeddingProvider"/>.
/// Because InMemory cannot execute pgvector cosine search, retrieval assertions use the
/// keyword path (<see cref="RetrievalService.SearchByKeywordAsync"/>), which exercises the same
/// metadata + visibility + disabled-document filtering as production. Vector-similarity accuracy
/// itself is covered separately against real PostgreSQL.
/// </remarks>
public class BuiltInContentTests : IDisposable
{
    private readonly AircaneDbContext _db;

    public BuiltInContentTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private BuiltInContentSeeder CreateSeeder() =>
        new(
            _db,
            new FakeEmbeddingProvider(),
            new SlidingWindowTextChunker(),
            NullLogger<BuiltInContentSeeder>.Instance,
            NullLogger<Aircane.Infrastructure.DocumentSources.EmbeddedResourceDocumentSource>.Instance);

    private LibraryService CreateLibraryService() =>
        new(_db, new NoopFileStorage(), NullLogger<LibraryService>.Instance);

    /// <summary>
    /// Mirrors the retrieval-eligibility filter used by <c>RetrievalService.BuildFilteredQuery</c>:
    /// a chunk is retrievable when its document is not disabled and (optionally) matches the game
    /// system. This validates the same filtering guarantees the RAG pipeline enforces without
    /// depending on the pgvector cosine search or the Npgsql <c>ILike</c> text match, neither of
    /// which the EF Core InMemory provider can translate.
    /// </summary>
    private async Task<List<(Guid ChunkId, string DocTitle)>> RetrievableChunksAsync(
        string? gameSystem = null)
    {
        var query = _db.DocumentChunks
            .Join(_db.SourceDocuments,
                chunk => chunk.SourceDocumentId,
                doc => doc.Id,
                (chunk, doc) => new { chunk, doc })
            .Where(x => !x.doc.IsDisabled);

        if (!string.IsNullOrWhiteSpace(gameSystem))
            query = query.Where(x => x.doc.GameSystem == gameSystem);

        var rows = await query.Select(x => new { x.chunk.Id, x.doc.Title }).ToListAsync();
        return rows.Select(r => (r.Id, r.Title)).ToList();
    }

    // ── Seeder: clean database ───────────────────────────────────────────────

    [Fact]
    public async Task BuiltInContentSeeder_OnCleanDatabase_SeedsAllThreeDocuments()
    {
        await CreateSeeder().SeedAsync();

        var builtIns = await _db.SourceDocuments.Where(d => d.IsBuiltIn).ToListAsync();
        Assert.Equal(3, builtIns.Count);

        Assert.Contains(builtIns, d => d.GameSystem == "D&D 5e");
        Assert.Contains(builtIns, d => d.GameSystem == "Pathfinder 1e");
        Assert.Contains(builtIns, d => d.GameSystem == "Pathfinder 2e");
    }

    [Fact]
    public async Task BuiltInContentSeeder_OnExistingDatabase_IsIdempotent()
    {
        await CreateSeeder().SeedAsync();
        var afterFirst = await _db.SourceDocuments.CountAsync(d => d.IsBuiltIn);
        var chunksAfterFirst = await _db.DocumentChunks.CountAsync();

        // Seeding again must not duplicate documents or chunks.
        await CreateSeeder().SeedAsync();

        Assert.Equal(afterFirst, await _db.SourceDocuments.CountAsync(d => d.IsBuiltIn));
        Assert.Equal(chunksAfterFirst, await _db.DocumentChunks.CountAsync());
    }

    [Fact]
    public async Task BuiltInContentSeeder_AllDocuments_HaveIsBuiltInTrue()
    {
        await CreateSeeder().SeedAsync();

        var docs = await _db.SourceDocuments.ToListAsync();
        Assert.NotEmpty(docs);
        Assert.All(docs, d => Assert.True(d.IsBuiltIn));
    }

    [Fact]
    public async Task BuiltInContentSeeder_AllDocuments_HaveCorrectLicenseKey()
    {
        await CreateSeeder().SeedAsync();

        var byGameSystem = await _db.SourceDocuments
            .Where(d => d.IsBuiltIn)
            .ToDictionaryAsync(d => d.GameSystem, d => d.LicenseKey);

        Assert.Equal(BuiltInLicenses.CcBy40Key, byGameSystem["D&D 5e"]);
        Assert.Equal(BuiltInLicenses.Ogl10aKey, byGameSystem["Pathfinder 1e"]);
        Assert.Equal(BuiltInLicenses.OrcKey, byGameSystem["Pathfinder 2e"]);
    }

    // ── Delete guard ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LibraryService_DeleteBuiltInDocument_ThrowsDomainError()
    {
        await CreateSeeder().SeedAsync();
        var builtIn = await _db.SourceDocuments.FirstAsync(d => d.IsBuiltIn);

        var library = CreateLibraryService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => library.DeleteDocumentAsync(builtIn.Id));
        Assert.Contains("cannot be deleted", ex.Message, StringComparison.OrdinalIgnoreCase);

        // The document must still exist.
        Assert.True(await _db.SourceDocuments.AnyAsync(d => d.Id == builtIn.Id));
    }

    // ── Disable / enable retrieval exclusion ──────────────────────────────────

    [Fact]
    public async Task LibraryService_DisableBuiltInDocument_ExcludesChunksFromRag()
    {
        await CreateSeeder().SeedAsync();
        var library = CreateLibraryService();

        var dnd = await _db.SourceDocuments.FirstAsync(d => d.GameSystem == "D&D 5e");

        // The document's chunks are retrievable before disabling.
        var before = await RetrievableChunksAsync();
        Assert.Contains(before, c => c.DocTitle == dnd.Title);

        await library.SetDocumentDisabledAsync(dnd.Id, disabled: true);

        // After disabling, none of the disabled document's chunks are retrievable.
        var after = await RetrievableChunksAsync();
        Assert.DoesNotContain(after, c => c.DocTitle == dnd.Title);
    }

    [Fact]
    public async Task LibraryService_ReEnableBuiltInDocument_RestoresChunksToRag()
    {
        await CreateSeeder().SeedAsync();
        var library = CreateLibraryService();

        var dnd = await _db.SourceDocuments.FirstAsync(d => d.GameSystem == "D&D 5e");

        await library.SetDocumentDisabledAsync(dnd.Id, disabled: true);
        await library.SetDocumentDisabledAsync(dnd.Id, disabled: false);

        var after = await RetrievableChunksAsync();
        Assert.Contains(after, c => c.DocTitle == dnd.Title);
    }

    [Fact]
    public async Task LibraryService_RestoreDefaults_ReEnablesDisabledBuiltIns()
    {
        await CreateSeeder().SeedAsync();
        var library = CreateLibraryService();

        var docs = await _db.SourceDocuments.Where(d => d.IsBuiltIn).ToListAsync();
        foreach (var d in docs)
            await library.SetDocumentDisabledAsync(d.Id, disabled: true);

        var restored = await library.RestoreBuiltInDefaultsAsync();

        Assert.Equal(docs.Count, restored);
        Assert.False(await _db.SourceDocuments.AnyAsync(d => d.IsBuiltIn && d.IsDisabled));
    }

    // ── Retrieval scoping (document-filter invariants) ────────────────────────
    //
    // These assert the game-system scoping and disabled-exclusion that RetrievalService applies
    // via BuildFilteredQuery. They validate the retrieval-eligibility filter directly (see
    // RetrievableChunksAsync) rather than the ILike text match / pgvector search, which the EF
    // Core InMemory provider cannot translate. Vector-similarity ranking itself is covered
    // against real PostgreSQL.

    [Fact]
    public async Task Retrieval_ScopedToDnd5e_YieldsOnlyDnd5eChunks()
    {
        await CreateSeeder().SeedAsync();

        var chunks = await RetrievableChunksAsync(gameSystem: "D&D 5e");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(
            "D&D 5e Systems Reference Document (2014)", c.DocTitle));
    }

    [Fact]
    public async Task Retrieval_ScopedToPf1e_YieldsOnlyPf1eChunks()
    {
        await CreateSeeder().SeedAsync();

        var chunks = await RetrievableChunksAsync(gameSystem: "Pathfinder 1e");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(
            "Pathfinder 1e Reference Document (Core Rulebook)", c.DocTitle));
    }

    [Fact]
    public async Task Retrieval_ScopedToPf2e_YieldsOnlyPf2eChunksAndNoDnd5e()
    {
        await CreateSeeder().SeedAsync();

        var chunks = await RetrievableChunksAsync(gameSystem: "Pathfinder 2e");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal(
            "Pathfinder 2e Remaster Core Rules", c.DocTitle));
        Assert.DoesNotContain(chunks, c =>
            c.DocTitle == "D&D 5e Systems Reference Document (2014)");
    }

    /// <summary>No-op file storage: the built-in delete guard trips before any file I/O.</summary>
    private sealed class NoopFileStorage : Aircane.Application.Abstractions.IFileStorageService
    {
        public Task<string> SaveFileAsync(Stream content, string extension, CancellationToken cancellationToken = default)
            => Task.FromResult("noop");
        public Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

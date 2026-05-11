using Aircane.Application.Abstractions;
using Aircane.Application.DocumentProcessing;
using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.DocumentProcessing;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.DocumentProcessing;

/// <summary>
/// Unit tests for <see cref="DocumentImportJob"/>.
/// Uses an in-memory EF Core database and a stub <see cref="IPdfTextExtractor"/>.
/// </summary>
public class DocumentImportJobTests
{
    // ── JSON documents ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_JsonDocument_SetsCompleted()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.json");

        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: []));
        await job.ProcessDocumentAsync(document.Id);

        var updated = await db.SourceDocuments.FindAsync(document.Id);
        Assert.Equal(ImportStatus.Completed, updated!.ImportStatus);
    }

    [Fact]
    public async Task ProcessDocumentAsync_JsonDocument_CreatesNoChunks()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "data.json");

        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: []));
        await job.ProcessDocumentAsync(document.Id);

        var chunks = await db.DocumentChunks
            .Where(c => c.SourceDocumentId == document.Id)
            .ToListAsync();

        Assert.Empty(chunks);
    }

    // ── PDF: OCR-required detection ───────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithOcrRequired_SetsOcrRequired()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "scanned.pdf");

        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: true, pages: []));
        await job.ProcessDocumentAsync(document.Id);

        var updated = await db.SourceDocuments.FindAsync(document.Id);
        Assert.Equal(ImportStatus.OcrRequired, updated!.ImportStatus);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithOcrRequired_CreatesNoChunks()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "scanned.pdf");

        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: true, pages: []));
        await job.ProcessDocumentAsync(document.Id);

        var chunks = await db.DocumentChunks
            .Where(c => c.SourceDocumentId == document.Id)
            .ToListAsync();

        Assert.Empty(chunks);
    }

    // ── PDF: normal extraction ────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_SetsCompleted()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.pdf");

        var pages = new[]
        {
            new PageText(1, "Page one content with enough text to pass the threshold.", 55),
            new PageText(2, "Page two content with enough text to pass the threshold.", 55),
        };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var updated = await db.SourceDocuments.FindAsync(document.Id);
        Assert.Equal(ImportStatus.Completed, updated!.ImportStatus);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithShortPages_CreatesOneChunkPerPage()
    {
        // Short page texts (well under the default 2000-char max) produce exactly one chunk each.
        using var db = CreateDb();
        var document = AddDocument(db, "adventure.pdf");

        var pages = new[]
        {
            new PageText(1, "First page text.", 16),
            new PageText(2, "Second page text.", 17),
            new PageText(3, "Third page text.", 16),
        };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunks = await db.DocumentChunks
            .Where(c => c.SourceDocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(3, chunks.Count);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[1].PageNumber);
        Assert.Equal(3, chunks[2].PageNumber);
        Assert.Equal("First page text.", chunks[0].Text);
        Assert.Equal("Second page text.", chunks[1].Text);
        Assert.Equal("Third page text.", chunks[2].Text);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_ChunksHaveCorrectChunkType()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.pdf");

        var pages = new[] { new PageText(1, "Some text content here.", 23) };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunk = await db.DocumentChunks
            .FirstAsync(c => c.SourceDocumentId == document.Id);

        Assert.Equal("page", chunk.ChunkType);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_ChunksInheritDocumentVisibility()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.pdf", visibility: ContentVisibility.DMOnly);

        var pages = new[] { new PageText(1, "Some text content here.", 23) };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunk = await db.DocumentChunks
            .FirstAsync(c => c.SourceDocumentId == document.Id);

        Assert.Equal(ContentVisibility.DMOnly, chunk.Visibility);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_ChunksHaveMetadataJson()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.pdf");

        var pages = new[] { new PageText(1, "Some text content here.", 23) };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunk = await db.DocumentChunks
            .FirstAsync(c => c.SourceDocumentId == document.Id);

        Assert.NotNull(chunk.MetadataJson);
        Assert.NotEqual("{}", chunk.MetadataJson);
        Assert.Contains("sourceDocumentId", chunk.MetadataJson);
        Assert.Contains("pageNumber", chunk.MetadataJson);
        Assert.Contains("chunkIndex", chunk.MetadataJson);
        Assert.Contains("sourceTitle", chunk.MetadataJson);
    }

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_ChunkIndicesAreSequentialAcrossPages()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "multi.pdf");

        var pages = new[]
        {
            new PageText(1, "Page one text.", 14),
            new PageText(2, "Page two text.", 14),
            new PageText(3, "Page three text.", 16),
        };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunks = await db.DocumentChunks
            .Where(c => c.SourceDocumentId == document.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    // ── Status transitions ────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_SetsProcessingBeforeExtraction()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules.pdf");

        ImportStatus? statusDuringExtraction = null;

        var capturingExtractor = new CapturingPdfExtractor(async () =>
        {
            // Read the status from DB while extraction is "in progress"
            var doc = await db.SourceDocuments.AsNoTracking().FirstAsync(d => d.Id == document.Id);
            statusDuringExtraction = doc.ImportStatus;
            return new PdfExtractionResult(
                Pages: new[] { new PageText(1, "Text content here.", 18) },
                IsOcrRequired: false,
                TotalCharacters: 18);
        });

        var job = CreateJob(db, capturingExtractor);
        await job.ProcessDocumentAsync(document.Id);

        Assert.Equal(ImportStatus.Processing, statusDuringExtraction);
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_DocumentNotFound_DoesNotThrow()
    {
        using var db = CreateDb();
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: []));

        // Should not throw even when document doesn't exist.
        await job.ProcessDocumentAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ProcessDocumentAsync_ExtractorThrows_SetsFailedStatus()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "broken.pdf");

        var throwingExtractor = new ThrowingPdfExtractor();
        var job = CreateJob(db, throwingExtractor);
        await job.ProcessDocumentAsync(document.Id);

        var updated = await db.SourceDocuments.FindAsync(document.Id);
        Assert.Equal(ImportStatus.Failed, updated!.ImportStatus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AircaneDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AircaneDbContext(options);
    }

    private static SourceDocument AddDocument(
        AircaneDbContext db,
        string fileName,
        ContentVisibility visibility = ContentVisibility.DMOnly)
    {
        // Create a real temp file so DocumentImportJob can open it.
        // The stub extractor ignores the stream content, but the FileStream must succeed.
        var tempPath = Path.GetTempPath();
        var tempFile = Path.Combine(tempPath, fileName);
        if (!File.Exists(tempFile))
            File.WriteAllBytes(tempFile, Array.Empty<byte>());

        var document = new SourceDocument(
            title: "Test Document",
            originalFileName: fileName,
            sourceType: SourceType.Rules,
            sourceMode: SourceMode.Upload,
            gameSystem: "D&D 5e",
            ruleset: "2014",
            sourcePath: fileName,
            visibility: visibility);

        db.SourceDocuments.Add(document);
        db.SaveChanges();
        return document;
    }

    private static DocumentImportJob CreateJob(
        AircaneDbContext db,
        IPdfTextExtractor extractor,
        IEmbeddingProvider? embeddingProvider = null)
    {
        // Use a stub IDocumentSource that opens files from the temp path.
        // The StubPdfExtractor ignores the stream content, so the path doesn't need to exist.
        return new DocumentImportJob(
            db,
            extractor,
            new SlidingWindowTextChunker(),
            embeddingProvider ?? new FakeEmbeddingProvider(),
            new NullLibraryHubNotifier(),
            new StubDocumentSource(),
            NullLogger<DocumentImportJob>.Instance);
    }

    // ── Embedding generation ──────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_PdfWithText_SetsEmbeddingsOnChunks()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules-embed.pdf");

        var pages = new[]
        {
            new PageText(1, "First page text.", 16),
            new PageText(2, "Second page text.", 17),
        };
        var job = CreateJob(db, new StubPdfExtractor(isOcrRequired: false, pages: pages));
        await job.ProcessDocumentAsync(document.Id);

        var chunks = await db.DocumentChunks
            .Where(c => c.SourceDocumentId == document.Id)
            .ToListAsync();

        Assert.All(chunks, c => Assert.NotNull(c.Embedding));
    }

    [Fact]
    public async Task ProcessDocumentAsync_WhenEmbeddingProviderFails_StillSetsCompleted()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules-embed-fail.pdf");

        var pages = new[] { new PageText(1, "Some text.", 10) };
        var job = CreateJob(
            db,
            new StubPdfExtractor(isOcrRequired: false, pages: pages),
            new ThrowingEmbeddingProvider());

        await job.ProcessDocumentAsync(document.Id);

        var updated = await db.SourceDocuments.FindAsync(document.Id);
        Assert.Equal(ImportStatus.Completed, updated!.ImportStatus);
    }

    [Fact]
    public async Task ProcessDocumentAsync_WhenEmbeddingProviderFails_ChunksHaveNullEmbedding()
    {
        using var db = CreateDb();
        var document = AddDocument(db, "rules-embed-null.pdf");

        var pages = new[] { new PageText(1, "Some text.", 10) };
        var job = CreateJob(
            db,
            new StubPdfExtractor(isOcrRequired: false, pages: pages),
            new ThrowingEmbeddingProvider());

        await job.ProcessDocumentAsync(document.Id);

        var chunk = await db.DocumentChunks
            .FirstAsync(c => c.SourceDocumentId == document.Id);

        Assert.Null(chunk.Embedding);
    }

    // ── Stub embedding providers ──────────────────────────────────────────────

    private sealed class ThrowingEmbeddingProvider : IEmbeddingProvider
    {
        public int Dimensions => 768;
        public string ProviderName => "Throwing";

        public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated embedding failure.");

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated embedding failure.");
    }

    // ── Stub extractors ───────────────────────────────────────────────────────

    private sealed class StubPdfExtractor : IPdfTextExtractor
    {
        private readonly bool _isOcrRequired;
        private readonly IReadOnlyList<PageText> _pages;

        public StubPdfExtractor(bool isOcrRequired, IEnumerable<PageText> pages)
        {
            _isOcrRequired = isOcrRequired;
            _pages = pages.ToList().AsReadOnly();
        }

        public Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
        {
            var totalChars = _pages.Sum(p => p.CharacterCount);
            return Task.FromResult(new PdfExtractionResult(
                Pages: _pages,
                IsOcrRequired: _isOcrRequired,
                TotalCharacters: totalChars));
        }
    }

    private sealed class ThrowingPdfExtractor : IPdfTextExtractor
    {
        public Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated extraction failure.");
    }

    private sealed class CapturingPdfExtractor : IPdfTextExtractor
    {
        private readonly Func<Task<PdfExtractionResult>> _factory;

        public CapturingPdfExtractor(Func<Task<PdfExtractionResult>> factory)
            => _factory = factory;

        public Task<PdfExtractionResult> ExtractTextAsync(Stream pdfStream, CancellationToken ct = default)
            => _factory();
    }

    // ── Stub hub notifier ─────────────────────────────────────────────────────

    private sealed class NullLibraryHubNotifier : ILibraryHubNotifier
    {
        public Task NotifyImportStatusUpdatedAsync(ImportStatusDto status, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    // ── Stub document source ──────────────────────────────────────────────────

    /// <summary>
    /// Stub <see cref="IDocumentSource"/> that returns an empty stream for any path.
    /// The <see cref="StubPdfExtractor"/> ignores stream content, so this is sufficient
    /// for all existing import job tests.
    /// </summary>
    private sealed class StubDocumentSource : IDocumentSource
    {
        public Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(
            string folderPath,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentSourceFile>>(Array.Empty<DocumentSourceFile>());

        public Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}

using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Library;
using Aircane.Application.Validation;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.DocumentProcessing;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Library;
using Aircane.Infrastructure.Persistence;
using Aircane.Workers.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// The host-chosen document thumbnail key: validation, upload, the PATCH operation, folder import, and
/// the guarantee that scans, re-index and built-in seeding never reset it.
/// </summary>
public sealed class DocumentThumbnailKeyTests : IDisposable
{
    private static readonly string FolderPath =
        Path.Combine(Path.GetTempPath(), "aircane-thumb-tests", Guid.NewGuid().ToString("N"));

    private readonly AircaneDbContext _db;
    private readonly RecordingImportJob _import = new();
    private readonly FakeDocumentSource _source = new();
    private readonly LibraryService _library;
    private readonly FolderScanJob _scan;

    public DocumentThumbnailKeyTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _library = new LibraryService(
            _db, new NoOpStorage(), new FakeEmbeddingProvider(), new GameSystemCanonicalizer(_db),
            _import, NullLogger<LibraryService>.Instance);

        _scan = new FolderScanJob(
            _source, _library, _import, new ScanCandidateAnalyzer(), _db, NullLogger<FolderScanJob>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("fantasy-knight-pf")]
    [InlineData("generic")]
    [InlineData("a")]
    [InlineData("grimdark-soldier-dw")]
    [InlineData("v2-art")]
    [InlineData("0")]
    public void Validator_AcceptsWellFormedKeys(string key)
    {
        Assert.True(ThumbnailKeyValidator.TryNormalize(key, out var normalized, out var error));
        Assert.Equal(key, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_TreatsEmptyAsAutomatic(string? input)
    {
        Assert.True(ThumbnailKeyValidator.TryNormalize(input, out var normalized, out _));
        Assert.Null(normalized);
    }

    [Theory]
    [InlineData("Fantasy-Knight")]
    [InlineData("fantasy_knight")]
    [InlineData("fantasy knight")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData(" padded")]
    [InlineData("padded ")]
    [InlineData("../etc/passwd")]
    [InlineData("a/b")]
    [InlineData("knight.webp")]
    [InlineData("knïght")]
    public void Validator_RejectsMalformedKeys(string key)
    {
        Assert.False(ThumbnailKeyValidator.TryNormalize(key, out _, out var error));
        Assert.Equal(ThumbnailKeyValidator.InvalidMessage, error);
    }

    [Fact]
    public void Validator_EnforcesTheLengthLimit()
    {
        Assert.True(ThumbnailKeyValidator.IsValid(new string('a', 64)));
        Assert.False(ThumbnailKeyValidator.IsValid(new string('a', 65)));
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    private Task<SourceDocumentDto> UploadAsync(string? thumbnailKey) =>
        _library.UploadDocumentAsync(new UploadDocumentRequest(
            Title: "Test Rules",
            OriginalFileName: "test-rules.pdf",
            SourceType: SourceType.Rules,
            GameSystem: "D&D 5e",
            Ruleset: "2014",
            Visibility: ContentVisibility.DMOnly,
            FileContent: new MemoryStream("%PDF-1.4 test"u8.ToArray()),
            ContentType: "application/pdf",
            ThumbnailKey: thumbnailKey));

    [Fact]
    public async Task Upload_WithAKey_StoresIt()
    {
        var dto = await UploadAsync("fantasy-knight-pf");

        Assert.Equal("fantasy-knight-pf", dto.ThumbnailKey);
        Assert.Equal("fantasy-knight-pf", (await _db.SourceDocuments.SingleAsync()).ThumbnailKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Upload_WithoutAKey_LeavesItNull(string? key)
    {
        var dto = await UploadAsync(key);

        Assert.Null(dto.ThumbnailKey);
        Assert.Null((await _db.SourceDocuments.SingleAsync()).ThumbnailKey);
    }

    [Fact]
    public async Task Upload_WithAnInvalidKey_IsRejected_AndCreatesNothing()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => UploadAsync("Not A Key"));

        Assert.Equal(ThumbnailKeyValidator.InvalidMessage, ex.Message);
        Assert.Equal(0, await _db.SourceDocuments.CountAsync());
    }

    // ── SetThumbnailAsync ─────────────────────────────────────────────────────

    private async Task<SourceDocument> SeedDocumentAsync(
        bool builtIn = false, string? thumbnailKey = null, string path = "storage/key")
    {
        var doc = new SourceDocument(
            "Doc", "doc.pdf", SourceType.Rules, SourceMode.Upload, "D&D 5e", "2014", path)
        {
            IsBuiltIn = builtIn,
            ThumbnailKey = thumbnailKey,
        };
        _db.SourceDocuments.Add(doc);
        await _db.SaveChangesAsync();
        return doc;
    }

    [Fact]
    public async Task SetThumbnail_SetsTheKey_AndReturnsTheUpdatedDocument()
    {
        var doc = await SeedDocumentAsync();

        var dto = await _library.SetThumbnailAsync(doc.Id, "space-opera-sw");

        Assert.Equal("space-opera-sw", dto.ThumbnailKey);
        Assert.Equal("space-opera-sw", (await _db.SourceDocuments.AsNoTracking().SingleAsync()).ThumbnailKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SetThumbnail_WithEmptyOrNull_ClearsTheKey(string? clear)
    {
        var doc = await SeedDocumentAsync(thumbnailKey: "space-opera-sw");

        var dto = await _library.SetThumbnailAsync(doc.Id, clear);

        Assert.Null(dto.ThumbnailKey);
        Assert.Null((await _db.SourceDocuments.AsNoTracking().SingleAsync()).ThumbnailKey);
    }

    [Fact]
    public async Task SetThumbnail_WithAnInvalidKey_Throws_AndKeepsTheOldKey()
    {
        var doc = await SeedDocumentAsync(thumbnailKey: "generic");

        await Assert.ThrowsAsync<ArgumentException>(() => _library.SetThumbnailAsync(doc.Id, "Bad Key!"));

        Assert.Equal("generic", (await _db.SourceDocuments.AsNoTracking().SingleAsync()).ThumbnailKey);
    }

    [Fact]
    public async Task SetThumbnail_ForAnUnknownDocument_ThrowsKeyNotFound() =>
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _library.SetThumbnailAsync(Guid.NewGuid(), "generic"));

    [Fact]
    public async Task SetThumbnail_WorksOnBuiltInDocuments()
    {
        var doc = await SeedDocumentAsync(builtIn: true);

        var dto = await _library.SetThumbnailAsync(doc.Id, "cavern-adventure-lm");

        Assert.Equal("cavern-adventure-lm", dto.ThumbnailKey);
        Assert.True(dto.IsBuiltIn);
    }

    // ── Never reset by scans, re-index or seeding ─────────────────────────────

    [Fact]
    public async Task Reindex_KeepsTheKey()
    {
        var doc = await SeedDocumentAsync(thumbnailKey: "gothic-vampire-vm");

        await _library.ReindexDocumentAsync(doc.Id);

        var after = await _db.SourceDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(ImportStatus.Pending, after.ImportStatus);
        Assert.Equal("gothic-vampire-vm", after.ThumbnailKey);
    }

    [Fact]
    public async Task FolderScan_ThatUpdatesAChangedDocument_KeepsTheKey()
    {
        var folder = new WatchedFolder("Books", FolderPath, SourceType.Rules, "D&D 5e", "2014");
        _db.WatchedFolders.Add(folder);
        var path = Path.Combine(FolderPath, "phb.pdf");
        var doc = new SourceDocument(
            "PHB", "phb.pdf", SourceType.Rules, SourceMode.FolderWatch, "D&D 5e", "2014", path,
            watchedFolderId: folder.Id, importStatus: ImportStatus.Completed)
        {
            ThumbnailKey = "fantasy-rulebook-dd",
        };
        _db.SourceDocuments.Add(doc);
        await _db.SaveChangesAsync();

        // The file changed after the document was last updated, so the scan re-imports it.
        _source.Files = [new DocumentSourceFile(path, "phb.pdf", DateTimeOffset.UtcNow.AddHours(1), 1000)];

        var result = await _scan.ScanFolderAsync(folder.Id);

        Assert.Equal(1, result.UpdatedFiles);
        var after = await _db.SourceDocuments.AsNoTracking().SingleAsync();
        Assert.Equal(ImportStatus.Pending, after.ImportStatus);
        Assert.Equal("fantasy-rulebook-dd", after.ThumbnailKey);
    }

    [Fact]
    public async Task FolderScan_WhenTheFileGoesMissing_KeepsTheKey()
    {
        var folder = new WatchedFolder("Books", FolderPath, SourceType.Rules, "D&D 5e", "2014");
        _db.WatchedFolders.Add(folder);
        var doc = new SourceDocument(
            "PHB", "phb.pdf", SourceType.Rules, SourceMode.FolderWatch, "D&D 5e", "2014",
            Path.Combine(FolderPath, "phb.pdf"), watchedFolderId: folder.Id, importStatus: ImportStatus.Completed)
        {
            ThumbnailKey = "fantasy-rulebook-dd",
        };
        _db.SourceDocuments.Add(doc);
        await _db.SaveChangesAsync();
        _source.Files = [];

        await _scan.ScanFolderAsync(folder.Id);

        var after = await _db.SourceDocuments.AsNoTracking().SingleAsync();
        Assert.False(after.IsSourceAvailable);
        Assert.Equal("fantasy-rulebook-dd", after.ThumbnailKey);
    }

    [Fact]
    public async Task BuiltInSeeding_DoesNotOverwriteAChosenKey()
    {
        await CreateSeeder().SeedAsync();
        var builtIn = await _db.SourceDocuments.FirstAsync(d => d.IsBuiltIn);
        Assert.Null(builtIn.ThumbnailKey);
        await _library.SetThumbnailAsync(builtIn.Id, "crystal-fantasy-dg");

        await CreateSeeder().SeedAsync();   // every startup runs the seeder again

        var after = await _db.SourceDocuments.AsNoTracking().SingleAsync(d => d.Id == builtIn.Id);
        Assert.Equal("crystal-fantasy-dg", after.ThumbnailKey);
    }

    private BuiltInContentSeeder CreateSeeder() => new(
        _db,
        new FakeEmbeddingProvider(),
        new SlidingWindowTextChunker(),
        NullLogger<BuiltInContentSeeder>.Instance,
        NullLogger<Aircane.Infrastructure.DocumentSources.EmbeddedResourceDocumentSource>.Instance);

    // ── Folder import: batch default and per-file keys ────────────────────────

    private async Task<WatchedFolder> SeedFolderWithFilesAsync(params string[] names)
    {
        var folder = new WatchedFolder("Books", FolderPath, SourceType.Rules, "D&D 5e", "2014");
        _db.WatchedFolders.Add(folder);
        await _db.SaveChangesAsync();
        _source.Files = names
            .Select(n => new DocumentSourceFile(Path.Combine(FolderPath, n), n, DateTimeOffset.UtcNow, 1000))
            .ToList();
        return folder;
    }

    [Fact]
    public async Task FolderImport_AppliesTheBatchDefault_AndPerFileKeysOverrideIt()
    {
        var folder = await SeedFolderWithFilesAsync("a.pdf", "b.pdf", "c.pdf");
        var request = new FolderImportSelectionRequest(
            folder.Id,
            [
                new FolderImportSelectionItem(Path.Combine(FolderPath, "a.pdf"), true),
                new FolderImportSelectionItem(Path.Combine(FolderPath, "b.pdf"), true, ThumbnailKey: "space-opera-sw"),
                new FolderImportSelectionItem(Path.Combine(FolderPath, "c.pdf"), true, ThumbnailKey: ""),
            ],
            DefaultThumbnailKey: "fantasy-knight-pf");

        await _scan.ImportSelectionAsync(request);

        var byName = await _db.SourceDocuments.AsNoTracking().ToDictionaryAsync(d => d.OriginalFileName);
        Assert.Equal("fantasy-knight-pf", byName["a.pdf"].ThumbnailKey);   // batch default
        Assert.Equal("space-opera-sw", byName["b.pdf"].ThumbnailKey);      // per-file override
        Assert.Equal("fantasy-knight-pf", byName["c.pdf"].ThumbnailKey);   // empty per-file key falls back to the default
    }

    [Fact]
    public async Task FolderImport_WithNoKeys_LeavesTheKeyNull()
    {
        var folder = await SeedFolderWithFilesAsync("a.pdf");

        await _scan.ImportSelectionAsync(new FolderImportSelectionRequest(
            folder.Id, [new FolderImportSelectionItem(Path.Combine(FolderPath, "a.pdf"), true)]));

        Assert.Null((await _db.SourceDocuments.AsNoTracking().SingleAsync()).ThumbnailKey);
    }

    [Fact]
    public void FolderImportValidator_RejectsABadBatchDefaultOrPerFileKey()
    {
        var validator = new FolderImportSelectionValidator();
        var item = new FolderImportSelectionItem("/x/a.pdf", true);

        Assert.True(validator.Validate(new FolderImportSelectionRequest(Guid.NewGuid(), [item], "generic")).IsValid);
        Assert.True(validator.Validate(new FolderImportSelectionRequest(Guid.NewGuid(), [item], "")).IsValid);
        Assert.False(validator.Validate(new FolderImportSelectionRequest(Guid.NewGuid(), [item], "Bad Key")).IsValid);
        Assert.False(validator.Validate(new FolderImportSelectionRequest(
            Guid.NewGuid(), [item with { ThumbnailKey = "../x" }])).IsValid);
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class FakeDocumentSource : IDocumentSource
    {
        public List<DocumentSourceFile> Files { get; set; } = [];

        public Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(string folderPath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentSourceFile>>(Files);

        public Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult(Files.Any(f => f.SourcePath == sourcePath));
    }

    private sealed class RecordingImportJob : IDocumentImportService
    {
        public Task<Guid> EnqueueImportJobAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<ImportStatusDto> GetJobStatusAsync(Guid documentId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportStatusDto(documentId, ImportStatus.Pending, null, null, DateTimeOffset.UtcNow));
    }

    private sealed class NoOpStorage : IFileStorageService
    {
        public Task<string> SaveFileAsync(Stream content, string extension, CancellationToken cancellationToken = default)
            => Task.FromResult($"/noop/{Guid.NewGuid()}{extension}");

        public Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

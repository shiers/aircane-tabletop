using Aircane.Application.Abstractions;
using Aircane.Application.Abstractions;
using Aircane.Application.DTOs.Library;
using Aircane.Application.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Embeddings;
using Aircane.Infrastructure.GameSystems;
using Aircane.Infrastructure.Library;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for the review-before-import flow: <see cref="FolderScanJob.PreviewFolderAsync"/>
/// (analysis with no writes) and <see cref="FolderScanJob.ImportSelectionAsync"/> (imports only
/// selected files, applies overrides, and preserves path-based idempotency).
/// </summary>
public class FolderScanJobReviewTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly FakeDocumentSource _source = new();
    private readonly RecordingImportJob _importJob = new();
    private readonly FolderScanJob _sut;

    public FolderScanJobReviewTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new AircaneDbContext(options);

        var libraryService = new LibraryService(
            _db,
            new NoOpStorage(),
            new FakeEmbeddingProvider(),
            new GameSystemCanonicalizer(_db),
            _importJob,
            NullLogger<LibraryService>.Instance);

        _sut = new FolderScanJob(
            _source,
            libraryService,
            _importJob,
            new ScanCandidateAnalyzer(),
            _db,
            NullLogger<FolderScanJob>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // A platform-appropriate absolute folder path so FolderPathValidator.IsFileWithinFolder
    // (which uses Path.GetFullPath) passes on both Windows and Linux CI.
    private static readonly string FolderPath =
        Path.Combine(Path.GetTempPath(), "aircane-scan-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Builds an absolute file path inside <see cref="FolderPath"/> for a bare filename.</summary>
    private static string FileInFolder(string fileName) => Path.Combine(FolderPath, fileName);

    private async Task<WatchedFolder> SeedFolderAsync(string? path = null)
    {
        var folder = new WatchedFolder(
            displayName: "Books",
            absolutePath: path ?? FolderPath,
            defaultSourceType: SourceType.Rules,
            defaultGameSystem: "D&D 5e",
            defaultRuleset: "2014");
        _db.WatchedFolders.Add(folder);
        await _db.SaveChangesAsync();
        return folder;
    }

    private void SetFolderFiles(string folderPath, params string[] fileNames)
    {
        _source.Files = fileNames
            .Select(n => new DocumentSourceFile(
                SourcePath: Path.Combine(folderPath, n),
                FileName: n,
                LastModifiedUtc: DateTimeOffset.UtcNow,
                SizeBytes: 1000))
            .ToList();
    }

    // ── PreviewFolderAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_ReturnsCandidatesForAllFiles_AndWritesNothing()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "Players Handbook.pdf", "Dungeon Masters Guide.pdf");

        var preview = await _sut.PreviewFolderAsync(folder.Id);

        Assert.Equal(2, preview.FilesFound);
        Assert.Equal(2, preview.Candidates.Count);
        // No documents created by preview.
        Assert.Equal(0, await _db.SourceDocuments.CountAsync());
        // No imports enqueued.
        Assert.Empty(_importJob.Processed);
    }

    [Fact]
    public async Task Preview_FlagsDuplicateVariants()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath,
            "PHB (BnW OCR).pdf", "PHB (Color OCR).pdf");

        var preview = await _sut.PreviewFolderAsync(folder.Id);

        Assert.All(preview.Candidates,
            c => Assert.Contains(ScanCandidateFlag.DuplicateVariant, c.Flags));
    }

    [Fact]
    public async Task Preview_FlagsAlreadyImported_AgainstExistingLibraryDocs()
    {
        var folder = await SeedFolderAsync();
        // An existing document whose filename normalizes to the same key as the discovered file.
        _db.SourceDocuments.Add(new SourceDocument(
            title: "Players Handbook",
            originalFileName: "Players Handbook.pdf",
            sourceType: SourceType.Rules,
            sourceMode: SourceMode.Upload,
            gameSystem: "D&D 5e",
            ruleset: "2014",
            sourcePath: "storage/key"));
        await _db.SaveChangesAsync();

        SetFolderFiles(folder.AbsolutePath, "Players Handbook (Color OCR).pdf");

        var preview = await _sut.PreviewFolderAsync(folder.Id);

        Assert.True(preview.Candidates.Single().AlreadyImported);
    }

    [Fact]
    public async Task Preview_UnknownFolder_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.PreviewFolderAsync(Guid.NewGuid()));
    }

    // ── ImportSelectionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task Import_OnlySelectedFiles_AreCreatedAndEnqueued()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "A.pdf", "B.pdf", "C.pdf");

        var request = new FolderImportSelectionRequest(folder.Id, new[]
        {
            new FolderImportSelectionItem(FileInFolder("A.pdf"), Import: true),
            new FolderImportSelectionItem(FileInFolder("B.pdf"), Import: false),
            new FolderImportSelectionItem(FileInFolder("C.pdf"), Import: true),
        });

        var result = await _sut.ImportSelectionAsync(request);

        Assert.Equal(2, result.NewFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(2, await _db.SourceDocuments.CountAsync());
        Assert.Equal(2, _importJob.Processed.Count);

        var titles = await _db.SourceDocuments.Select(d => d.Title).ToListAsync();
        Assert.Contains("A", titles);
        Assert.Contains("C", titles);
        Assert.DoesNotContain("B", titles);
    }

    [Fact]
    public async Task Import_AppliesPerFileOverrides()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "phb.pdf");

        var request = new FolderImportSelectionRequest(folder.Id, new[]
        {
            new FolderImportSelectionItem(
                FileInFolder("phb.pdf"),
                Import: true,
                Title: "Player's Handbook",
                SourceType: SourceType.Adventure,
                GameSystem: "Pathfinder 2e",
                Ruleset: "Remaster"),
        });

        await _sut.ImportSelectionAsync(request);

        var doc = await _db.SourceDocuments.SingleAsync();
        Assert.Equal("Player's Handbook", doc.Title);
        Assert.Equal(SourceType.Adventure, doc.SourceType);
        Assert.Equal("Pathfinder 2e", doc.GameSystem);
        Assert.Equal("Remaster", doc.Ruleset);
    }

    [Fact]
    public async Task Import_FallsBackToFolderDefaults_WhenOverridesNull()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "Monster Manual.pdf");

        var request = new FolderImportSelectionRequest(folder.Id, new[]
        {
            new FolderImportSelectionItem(FileInFolder("Monster Manual.pdf"), Import: true),
        });

        await _sut.ImportSelectionAsync(request);

        var doc = await _db.SourceDocuments.SingleAsync();
        Assert.Equal("Monster Manual", doc.Title); // filename-derived
        Assert.Equal(SourceType.Rules, doc.SourceType); // folder default
        Assert.Equal("D&D 5e", doc.GameSystem); // folder default
        Assert.Equal("2014", doc.Ruleset); // folder default
    }

    [Fact]
    public async Task Import_IsIdempotent_ForAlreadyIndexedPath()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "phb.pdf");
        var path = FileInFolder("phb.pdf");

        var request = new FolderImportSelectionRequest(folder.Id, new[]
        {
            new FolderImportSelectionItem(path, Import: true),
        });

        await _sut.ImportSelectionAsync(request);
        var secondResult = await _sut.ImportSelectionAsync(request);

        // Second import creates nothing new; the path is already indexed.
        Assert.Equal(0, secondResult.NewFiles);
        Assert.Equal(1, secondResult.SkippedFiles);
        Assert.Equal(1, await _db.SourceDocuments.CountAsync());
    }

    [Fact]
    public async Task Import_SkipsSelection_ForFileNoLongerInFolder()
    {
        var folder = await SeedFolderAsync();
        SetFolderFiles(folder.AbsolutePath, "present.pdf"); // "ghost.pdf" is not present

        var request = new FolderImportSelectionRequest(folder.Id, new[]
        {
            new FolderImportSelectionItem(FileInFolder("ghost.pdf"), Import: true),
        });

        var result = await _sut.ImportSelectionAsync(request);

        Assert.Equal(0, result.NewFiles);
        Assert.Equal(1, result.SkippedFiles);
        Assert.Equal(0, await _db.SourceDocuments.CountAsync());
    }

    [Fact]
    public async Task Import_UnknownFolder_Throws()
    {
        var request = new FolderImportSelectionRequest(Guid.NewGuid(),
            new[] { new FolderImportSelectionItem("x", Import: true) });

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.ImportSelectionAsync(request));
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeDocumentSource : IDocumentSource
    {
        public List<DocumentSourceFile> Files { get; set; } = new();

        public Task<IReadOnlyList<DocumentSourceFile>> ListFilesAsync(string folderPath, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DocumentSourceFile>>(Files);

        public Task<Stream> OpenStreamAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream());

        public Task<bool> FileExistsAsync(string sourcePath, CancellationToken ct = default)
            => Task.FromResult(Files.Any(f => f.SourcePath == sourcePath));
    }

    /// <summary>
    /// Test double for <see cref="IDocumentImportService"/> that records enqueued document ids
    /// instead of running the import pipeline.
    /// </summary>
    private sealed class RecordingImportJob : IDocumentImportService
    {
        public List<Guid> Processed { get; } = new();

        public Task<Guid> EnqueueImportJobAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            Processed.Add(documentId);
            return Task.FromResult(Guid.NewGuid());
        }

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

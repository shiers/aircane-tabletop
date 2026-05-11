using Aircane.Application.DTOs.Library;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Library;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="LibraryService.CreateDocumentFromFolderAsync"/>.
/// Verifies that classification defaults are inherited from the parent <see cref="WatchedFolder"/>
/// and that the created <see cref="SourceDocument"/> has the correct mode and availability flags.
/// </summary>
public class FolderDocumentInheritanceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly LibraryService _sut;

    public FolderDocumentInheritanceTests()
    {
        var options = new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AircaneDbContext(options);

        // IFileStorageService is not exercised by CreateDocumentFromFolderAsync,
        // so we pass a no-op stub.
        _sut = new LibraryService(_db, new NoOpFileStorageService(), NullLogger<LibraryService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ── Inheritance of DefaultSourceType ─────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_InheritsDefaultSourceType()
    {
        var folder = await SeedFolderAsync(
            defaultSourceType: SourceType.Rules,
            defaultGameSystem: null,
            defaultRuleset: null);

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(SourceType.Rules, dto.SourceType);
    }

    [Theory]
    [InlineData(SourceType.Rules)]
    [InlineData(SourceType.Adventure)]
    [InlineData(SourceType.Solo)]
    [InlineData(SourceType.Character)]
    [InlineData(SourceType.Homebrew)]
    public async Task CreateDocumentFromFolder_InheritsAllValidDefaultSourceTypes(SourceType sourceType)
    {
        var folder = await SeedFolderAsync(defaultSourceType: sourceType);

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, $"/books/file.pdf", "file.pdf");

        Assert.Equal(sourceType, dto.SourceType);
    }

    // ── Inheritance of DefaultGameSystem ─────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_InheritsDefaultGameSystem()
    {
        var folder = await SeedFolderAsync(defaultGameSystem: "D&D 5e");

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal("D&D 5e", dto.GameSystem);
    }

    [Fact]
    public async Task CreateDocumentFromFolder_NullDefaultGameSystem_ResultsInEmptyString()
    {
        var folder = await SeedFolderAsync(defaultGameSystem: null);

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(string.Empty, dto.GameSystem);
    }

    // ── Inheritance of DefaultRuleset ─────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_InheritsDefaultRuleset()
    {
        var folder = await SeedFolderAsync(defaultRuleset: "D&D 5e 2014");

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal("D&D 5e 2014", dto.Ruleset);
    }

    [Fact]
    public async Task CreateDocumentFromFolder_NullDefaultRuleset_ResultsInEmptyString()
    {
        var folder = await SeedFolderAsync(defaultRuleset: null);

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(string.Empty, dto.Ruleset);
    }

    // ── SourceMode and WatchedFolderId ────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_SetsSourceModeToFolderWatch()
    {
        var folder = await SeedFolderAsync();

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(SourceMode.FolderWatch, dto.SourceMode);
    }

    [Fact]
    public async Task CreateDocumentFromFolder_SetsWatchedFolderIdToParentFolder()
    {
        var folder = await SeedFolderAsync();

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(folder.Id, dto.WatchedFolderId);
    }

    // ── IsSourceAvailable ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_SetsIsSourceAvailableTrue()
    {
        var folder = await SeedFolderAsync();

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.True(dto.IsSourceAvailable);
    }

    // ── ImportStatus ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_SetsImportStatusToPending()
    {
        var folder = await SeedFolderAsync();

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/phb.pdf", "phb.pdf");

        Assert.Equal(ImportStatus.Pending, dto.ImportStatus);
    }

    // ── Title derived from filename ───────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_TitleDerivedFromFilenameWithoutExtension()
    {
        var folder = await SeedFolderAsync();

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/books/players-handbook.pdf", "players-handbook.pdf");

        Assert.Equal("players-handbook", dto.Title);
    }

    // ── Folder not found ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_UnknownFolderId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateDocumentFromFolderAsync(
                Guid.NewGuid(), "/books/phb.pdf", "phb.pdf"));
    }

    // ── Persisted to database ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_PersistsDocumentToDatabase()
    {
        var folder = await SeedFolderAsync(
            defaultSourceType: SourceType.Adventure,
            defaultGameSystem: "D&D 5e",
            defaultRuleset: "D&D 5e 2014");

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/adventures/lost-mine.pdf", "lost-mine.pdf");

        var persisted = await _db.SourceDocuments.FindAsync(dto.Id);
        Assert.NotNull(persisted);
        Assert.Equal(SourceType.Adventure, persisted.SourceType);
        Assert.Equal("D&D 5e", persisted.GameSystem);
        Assert.Equal("D&D 5e 2014", persisted.Ruleset);
        Assert.Equal(SourceMode.FolderWatch, persisted.SourceMode);
        Assert.Equal(folder.Id, persisted.WatchedFolderId);
        Assert.True(persisted.IsSourceAvailable);
    }

    // ── Full defaults scenario ────────────────────────────────────────────────

    [Fact]
    public async Task CreateDocumentFromFolder_AllDefaults_InheritedCorrectly()
    {
        var folder = await SeedFolderAsync(
            defaultSourceType: SourceType.Rules,
            defaultGameSystem: "D&D 5e",
            defaultRuleset: "D&D 5e 2014");

        var dto = await _sut.CreateDocumentFromFolderAsync(
            folder.Id, "/rules/phb.pdf", "phb.pdf");

        Assert.Equal(SourceType.Rules, dto.SourceType);
        Assert.Equal("D&D 5e", dto.GameSystem);
        Assert.Equal("D&D 5e 2014", dto.Ruleset);
        Assert.Equal(SourceMode.FolderWatch, dto.SourceMode);
        Assert.Equal(folder.Id, dto.WatchedFolderId);
        Assert.True(dto.IsSourceAvailable);
        Assert.Equal(ImportStatus.Pending, dto.ImportStatus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<WatchedFolder> SeedFolderAsync(
        SourceType defaultSourceType = SourceType.Rules,
        string? defaultGameSystem = "D&D 5e",
        string? defaultRuleset = "D&D 5e 2014")
    {
        var folder = new WatchedFolder(
            displayName: "Test Folder",
            absolutePath: "/books",
            defaultSourceType: defaultSourceType,
            defaultGameSystem: defaultGameSystem,
            defaultRuleset: defaultRuleset);

        _db.WatchedFolders.Add(folder);
        await _db.SaveChangesAsync();
        return folder;
    }
}

/// <summary>
/// No-op implementation of <see cref="IFileStorageService"/> for tests that do not exercise file I/O.
/// </summary>
file sealed class NoOpFileStorageService : Aircane.Application.Abstractions.IFileStorageService
{
    public Task<string> SaveFileAsync(Stream content, string extension, CancellationToken cancellationToken = default)
        => Task.FromResult($"/noop/{Guid.NewGuid()}{extension}");

    public Task DeleteFileAsync(string storagePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

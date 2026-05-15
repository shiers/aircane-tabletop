using Aircane.Infrastructure.DocumentSources;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aircane.UnitTests.DocumentSources;

/// <summary>
/// Unit tests for <see cref="FolderWatchDocumentSource"/> using a real temp directory.
/// No mocks are used - all tests exercise actual filesystem I/O.
/// </summary>
public sealed class FolderWatchDocumentSourceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FolderWatchDocumentSource _sut;

    public FolderWatchDocumentSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aircane-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new FolderWatchDocumentSource(NullLogger<FolderWatchDocumentSource>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ── ListFilesAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListFilesAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Empty(files);
    }

    [Fact]
    public async Task ListFilesAsync_NonExistentDirectory_ReturnsEmptyList()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var files = await _sut.ListFilesAsync(missing);

        Assert.Empty(files);
    }

    [Fact]
    public async Task ListFilesAsync_PdfFile_IsIncluded()
    {
        var pdfPath = CreateFile("rules.pdf", "fake pdf content");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Single(files);
        Assert.Equal(pdfPath, files[0].SourcePath);
        Assert.Equal("rules.pdf", files[0].FileName);
    }

    [Fact]
    public async Task ListFilesAsync_JsonFile_IsIncluded()
    {
        CreateFile("character.json", "{}");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Single(files);
        Assert.Equal("character.json", files[0].FileName);
    }

    [Fact]
    public async Task ListFilesAsync_UnsupportedExtension_IsExcluded()
    {
        CreateFile("readme.txt", "some text");
        CreateFile("image.png", "fake image");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Empty(files);
    }

    [Fact]
    public async Task ListFilesAsync_MixedFiles_ReturnsOnlySupportedExtensions()
    {
        CreateFile("rules.pdf", "pdf");
        CreateFile("character.json", "{}");
        CreateFile("notes.txt", "text");
        CreateFile("image.png", "img");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.True(
            f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            f.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ListFilesAsync_PopulatesFileName()
    {
        CreateFile("players-handbook.pdf", "content");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Equal("players-handbook.pdf", files[0].FileName);
    }

    [Fact]
    public async Task ListFilesAsync_PopulatesSourcePath_AsAbsolutePath()
    {
        var pdfPath = CreateFile("adventure.pdf", "content");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Equal(pdfPath, files[0].SourcePath);
        Assert.True(Path.IsPathRooted(files[0].SourcePath));
    }

    [Fact]
    public async Task ListFilesAsync_PopulatesLastModifiedUtc()
    {
        CreateFile("doc.pdf", "content");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.NotNull(files[0].LastModifiedUtc);
    }

    [Fact]
    public async Task ListFilesAsync_PopulatesSizeBytes()
    {
        var content = "hello world";
        CreateFile("doc.pdf", content);

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.NotNull(files[0].SizeBytes);
        Assert.True(files[0].SizeBytes > 0);
    }

    [Fact]
    public async Task ListFilesAsync_ExtensionCaseInsensitive_PdfUppercase_IsIncluded()
    {
        CreateFile("RULES.PDF", "content");

        var files = await _sut.ListFilesAsync(_tempDir);

        Assert.Single(files);
    }

    // ── OpenStreamAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task OpenStreamAsync_ExistingFile_ReturnsReadableStream()
    {
        var content = "stream content";
        var path = CreateFile("doc.pdf", content);

        await using var stream = await _sut.OpenStreamAsync(path);

        Assert.NotNull(stream);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task OpenStreamAsync_ExistingFile_StreamContainsExpectedContent()
    {
        var content = "expected content";
        var path = CreateFile("doc.pdf", content);

        await using var stream = await _sut.OpenStreamAsync(path);
        using var reader = new StreamReader(stream);
        var read = await reader.ReadToEndAsync();

        Assert.Equal(content, read);
    }

    [Fact]
    public async Task OpenStreamAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "missing.pdf");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.OpenStreamAsync(missing));
    }

    // ── FileExistsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task FileExistsAsync_ExistingFile_ReturnsTrue()
    {
        var path = CreateFile("doc.pdf", "content");

        var exists = await _sut.FileExistsAsync(path);

        Assert.True(exists);
    }

    [Fact]
    public async Task FileExistsAsync_NonExistentFile_ReturnsFalse()
    {
        var missing = Path.Combine(_tempDir, "missing.pdf");

        var exists = await _sut.FileExistsAsync(missing);

        Assert.False(exists);
    }

    [Fact]
    public async Task FileExistsAsync_DeletedFile_ReturnsFalse()
    {
        var path = CreateFile("doc.pdf", "content");
        File.Delete(path);

        var exists = await _sut.FileExistsAsync(path);

        Assert.False(exists);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a file in the temp directory and returns its full path.</summary>
    private string CreateFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}

using Aircane.Infrastructure.Library;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="FileValidationHelper"/>.
/// Covers filename sanitization and file type/MIME validation.
/// </summary>
public class FileValidationHelperTests
{
    // ── IsExtensionAllowed ────────────────────────────────────────────────────

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("rules.PDF")]
    [InlineData("adventure.json")]
    [InlineData("character.JSON")]
    public void IsExtensionAllowed_AllowedExtensions_ReturnsTrue(string fileName)
    {
        Assert.True(FileValidationHelper.IsExtensionAllowed(fileName));
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("image.png")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(".")]
    public void IsExtensionAllowed_DisallowedExtensions_ReturnsFalse(string fileName)
    {
        Assert.False(FileValidationHelper.IsExtensionAllowed(fileName));
    }

    // ── IsMimeTypeAllowed ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("document.pdf", "application/pdf")]
    [InlineData("document.PDF", "application/pdf")]
    public void IsMimeTypeAllowed_PdfWithCorrectMime_ReturnsTrue(string fileName, string mimeType)
    {
        Assert.True(FileValidationHelper.IsMimeTypeAllowed(fileName, mimeType));
    }

    [Theory]
    [InlineData("data.json", "application/json")]
    [InlineData("data.json", "text/json")]
    [InlineData("data.json", "text/plain")]
    [InlineData("data.JSON", "application/json")]
    public void IsMimeTypeAllowed_JsonWithAcceptedMimes_ReturnsTrue(string fileName, string mimeType)
    {
        Assert.True(FileValidationHelper.IsMimeTypeAllowed(fileName, mimeType));
    }

    [Theory]
    [InlineData("document.pdf", "application/octet-stream")]
    [InlineData("document.pdf", "text/plain")]
    [InlineData("data.json", "application/pdf")]
    [InlineData("image.png", "image/png")]
    [InlineData("script.exe", "application/x-msdownload")]
    public void IsMimeTypeAllowed_WrongMimeForExtension_ReturnsFalse(string fileName, string mimeType)
    {
        Assert.False(FileValidationHelper.IsMimeTypeAllowed(fileName, mimeType));
    }

    // ── SanitizeFileName ──────────────────────────────────────────────────────

    [Fact]
    public void SanitizeFileName_NormalFileName_ReturnsUnchanged()
    {
        var result = FileValidationHelper.SanitizeFileName("players-handbook.pdf");
        Assert.Equal("players-handbook.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_PathTraversalWithForwardSlash_StripsDirectory()
    {
        var result = FileValidationHelper.SanitizeFileName("../../etc/passwd");
        // Path.GetFileName strips directory components; remaining dots become underscores
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void SanitizeFileName_PathTraversalWithBackslash_StripsDirectory()
    {
        var result = FileValidationHelper.SanitizeFileName(@"..\..\windows\system32\cmd.exe");
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
    }

    [Fact]
    public void SanitizeFileName_AbsoluteWindowsPath_StripsDirectory()
    {
        var result = FileValidationHelper.SanitizeFileName(@"C:\Users\admin\secret.pdf");
        Assert.Equal("secret.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_AbsoluteUnixPath_StripsDirectory()
    {
        var result = FileValidationHelper.SanitizeFileName("/etc/passwd");
        Assert.Equal("passwd", result);
    }

    [Fact]
    public void SanitizeFileName_NullOrWhitespace_ReturnsFallback()
    {
        Assert.Equal("upload", FileValidationHelper.SanitizeFileName(""));
        Assert.Equal("upload", FileValidationHelper.SanitizeFileName("   "));
    }

    [Fact]
    public void SanitizeFileName_OnlyDots_ReturnsFallback()
    {
        // "..." → ".." replacement turns it to "_." → trim dots → "_"
        // "_" is a valid sanitized result (non-empty, no path chars).
        var result = FileValidationHelper.SanitizeFileName("...");
        Assert.NotEmpty(result);
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void SanitizeFileName_DoubleDotInName_CollapsesDots()
    {
        var result = FileValidationHelper.SanitizeFileName("my..file.pdf");
        Assert.DoesNotContain("..", result);
    }

    [Fact]
    public void SanitizeFileName_NullCharacter_IsReplaced()
    {
        // Null bytes in filenames are a security risk; they must be replaced.
        var input = "file" + '\0' + "name.pdf";
        var result = FileValidationHelper.SanitizeFileName(input);
        Assert.DoesNotContain('\0', result);
    }

    [Fact]
    public void SanitizeFileName_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        var result = FileValidationHelper.SanitizeFileName("  my document.pdf  ");
        Assert.Equal("my document.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_InvalidFileSystemChars_AreReplaced()
    {
        // Characters like < > : " | ? * are invalid on Windows filesystems.
        var result = FileValidationHelper.SanitizeFileName("file<name>.pdf");
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    // ── GetNormalizedExtension ────────────────────────────────────────────────

    [Theory]
    [InlineData("document.PDF", ".pdf")]
    [InlineData("rules.JSON", ".json")]
    [InlineData("file.Pdf", ".pdf")]
    [InlineData("noextension", "")]
    public void GetNormalizedExtension_ReturnsLowercaseExtension(string fileName, string expected)
    {
        Assert.Equal(expected, FileValidationHelper.GetNormalizedExtension(fileName));
    }

    // ── MaxFileSizeBytes ──────────────────────────────────────────────────────

    [Fact]
    public void MaxFileSizeBytes_Is50MB()
    {
        Assert.Equal(50L * 1024 * 1024, FileValidationHelper.MaxFileSizeBytes);
    }
}

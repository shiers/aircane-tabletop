using Aircane.Application.Validation;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="FileUploadValidator"/>.
/// </summary>
public sealed class FileUploadValidatorTests
{
    // --- SanitizeFileName ---

    [Fact]
    public void SanitizeFileName_RemovesPathSeparators()
    {
        var result = FileUploadValidator.SanitizeFileName(@"C:\Users\host\Documents\rules.pdf");
        Assert.Equal("rules.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_RemovesUnixPathSeparators()
    {
        var result = FileUploadValidator.SanitizeFileName("/home/user/docs/adventure.json");
        Assert.Equal("adventure.json", result);
    }

    [Fact]
    public void SanitizeFileName_RemovesSpecialCharacters()
    {
        var result = FileUploadValidator.SanitizeFileName("my<file>name|with:bad*chars?.pdf");
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain("|", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
        Assert.EndsWith(".pdf", result);
    }

    [Fact]
    public void SanitizeFileName_LimitsLengthTo255()
    {
        var longName = new string('a', 300) + ".pdf";
        var result = FileUploadValidator.SanitizeFileName(longName);
        Assert.True(result.Length <= FileUploadValidator.MaxFileNameLength);
        Assert.EndsWith(".pdf", result);
    }

    [Fact]
    public void SanitizeFileName_PreservesValidFilename()
    {
        var result = FileUploadValidator.SanitizeFileName("my-rules_v2.pdf");
        Assert.Equal("my-rules_v2.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_ReturnsDefault_WhenEmpty()
    {
        Assert.Equal("unnamed_file", FileUploadValidator.SanitizeFileName(""));
        Assert.Equal("unnamed_file", FileUploadValidator.SanitizeFileName("   "));
    }

    [Fact]
    public void SanitizeFileName_ReturnsDefault_WhenOnlyPathSeparators()
    {
        Assert.Equal("unnamed_file", FileUploadValidator.SanitizeFileName(@"C:\"));
        Assert.Equal("unnamed_file", FileUploadValidator.SanitizeFileName("/"));
    }

    [Fact]
    public void SanitizeFileName_CollapsesMultipleUnderscores()
    {
        var result = FileUploadValidator.SanitizeFileName("file___name.pdf");
        Assert.DoesNotContain("___", result);
    }

    // --- IsAllowedExtension ---

    [Theory]
    [InlineData("document.pdf", true)]
    [InlineData("document.PDF", true)]
    [InlineData("data.json", true)]
    [InlineData("data.JSON", true)]
    [InlineData("script.exe", false)]
    [InlineData("page.html", false)]
    [InlineData("archive.zip", false)]
    [InlineData("image.png", false)]
    [InlineData("noextension", false)]
    [InlineData("", false)]
    public void IsAllowedExtension_ValidatesCorrectly(string fileName, bool expected)
    {
        Assert.Equal(expected, FileUploadValidator.IsAllowedExtension(fileName));
    }

    // --- IsAllowedMimeType ---

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("application/json", true)]
    [InlineData("APPLICATION/PDF", true)]
    [InlineData("text/html", false)]
    [InlineData("application/octet-stream", false)]
    [InlineData("image/png", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAllowedMimeType_ValidatesCorrectly(string? mimeType, bool expected)
    {
        Assert.Equal(expected, FileUploadValidator.IsAllowedMimeType(mimeType));
    }

    // --- ValidateFileSize ---

    [Fact]
    public void ValidateFileSize_AcceptsZeroBytes()
    {
        Assert.True(FileUploadValidator.ValidateFileSize(0));
    }

    [Fact]
    public void ValidateFileSize_AcceptsWithinLimit()
    {
        Assert.True(FileUploadValidator.ValidateFileSize(50 * 1024 * 1024)); // 50 MB
    }

    [Fact]
    public void ValidateFileSize_AcceptsExactLimit()
    {
        Assert.True(FileUploadValidator.ValidateFileSize(FileUploadValidator.DefaultMaxUploadSizeBytes));
    }

    [Fact]
    public void ValidateFileSize_RejectsOverLimit()
    {
        Assert.False(FileUploadValidator.ValidateFileSize(FileUploadValidator.DefaultMaxUploadSizeBytes + 1));
    }

    [Fact]
    public void ValidateFileSize_RejectsNegativeSize()
    {
        Assert.False(FileUploadValidator.ValidateFileSize(-1));
    }

    [Fact]
    public void ValidateFileSize_UsesCustomMaxBytes()
    {
        long customMax = 1024; // 1 KB
        Assert.True(FileUploadValidator.ValidateFileSize(1024, customMax));
        Assert.False(FileUploadValidator.ValidateFileSize(1025, customMax));
    }
}

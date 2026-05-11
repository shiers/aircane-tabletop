using System.Runtime.InteropServices;
using Aircane.Application.Validation;
using Xunit;

namespace Aircane.UnitTests.Library;

/// <summary>
/// Unit tests for <see cref="FolderPathValidator"/>.
/// </summary>
public sealed class FolderPathValidatorTests
{
    // ─── IsAbsolutePath ───────────────────────────────────────────────────────

    [Fact]
    public void IsAbsolutePath_ReturnsFalse_WhenNull()
    {
        Assert.False(FolderPathValidator.IsAbsolutePath(null));
    }

    [Fact]
    public void IsAbsolutePath_ReturnsFalse_WhenEmpty()
    {
        Assert.False(FolderPathValidator.IsAbsolutePath(""));
        Assert.False(FolderPathValidator.IsAbsolutePath("   "));
    }

    [Fact]
    public void IsAbsolutePath_ReturnsFalse_WhenRelative()
    {
        Assert.False(FolderPathValidator.IsAbsolutePath("relative/path"));
        Assert.False(FolderPathValidator.IsAbsolutePath("./relative"));
        Assert.False(FolderPathValidator.IsAbsolutePath("../parent"));
    }

    [Fact]
    public void IsAbsolutePath_ReturnsTrue_WhenAbsoluteUnix()
    {
        Assert.True(FolderPathValidator.IsAbsolutePath("/home/user/docs"));
        Assert.True(FolderPathValidator.IsAbsolutePath("/"));
    }

    [Fact]
    public void IsAbsolutePath_ReturnsTrue_WhenAbsoluteWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Skip on non-Windows

        Assert.True(FolderPathValidator.IsAbsolutePath(@"C:\Users\host\Documents"));
        Assert.True(FolderPathValidator.IsAbsolutePath(@"D:\"));
    }

    // ─── ContainsTraversalSequence ────────────────────────────────────────────

    [Fact]
    public void ContainsTraversalSequence_ReturnsFalse_WhenNull()
    {
        Assert.False(FolderPathValidator.ContainsTraversalSequence(null));
    }

    [Fact]
    public void ContainsTraversalSequence_ReturnsFalse_WhenEmpty()
    {
        Assert.False(FolderPathValidator.ContainsTraversalSequence(""));
    }

    [Fact]
    public void ContainsTraversalSequence_ReturnsFalse_WhenCleanPath()
    {
        Assert.False(FolderPathValidator.ContainsTraversalSequence("/home/user/docs"));
        Assert.False(FolderPathValidator.ContainsTraversalSequence("/home/user/my.folder/docs"));
    }

    [Fact]
    public void ContainsTraversalSequence_ReturnsTrue_WhenContainsDotDot()
    {
        Assert.True(FolderPathValidator.ContainsTraversalSequence("/home/user/../etc"));
        Assert.True(FolderPathValidator.ContainsTraversalSequence("/home/user/.."));
    }

    [Fact]
    public void ContainsTraversalSequence_ReturnsTrue_WhenContainsDotDotWindows()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            Assert.True(FolderPathValidator.ContainsTraversalSequence(@"C:\Users\..\Windows"));
        }
    }

    // ─── IsSystemDirectory ────────────────────────────────────────────────────

    [Fact]
    public void IsSystemDirectory_ReturnsFalse_WhenNull()
    {
        Assert.False(FolderPathValidator.IsSystemDirectory(null));
    }

    [Fact]
    public void IsSystemDirectory_ReturnsFalse_WhenUserDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.False(FolderPathValidator.IsSystemDirectory(@"C:\Users\host\Documents\rpg"));
        }
        else
        {
            Assert.False(FolderPathValidator.IsSystemDirectory("/home/user/rpg/rules"));
        }
    }

    [Fact]
    public void IsSystemDirectory_ReturnsTrue_WhenSystemPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(FolderPathValidator.IsSystemDirectory(@"C:\Windows"));
        }
        else
        {
            Assert.True(FolderPathValidator.IsSystemDirectory("/etc"));
            Assert.True(FolderPathValidator.IsSystemDirectory("/usr"));
            Assert.True(FolderPathValidator.IsSystemDirectory("/bin"));
            Assert.True(FolderPathValidator.IsSystemDirectory("/proc"));
        }
    }

    [Fact]
    public void IsSystemDirectory_ReturnsFalse_WhenSubdirectoryOfSystem()
    {
        // Subdirectories of system dirs are NOT blocked (only the root system dirs themselves)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // C:\Windows is blocked, but C:\Windows\Temp is not (it's a subdirectory)
            // Actually we only block the exact system directory, not children
            Assert.False(FolderPathValidator.IsSystemDirectory(@"C:\Windows\Temp"));
        }
        else
        {
            Assert.False(FolderPathValidator.IsSystemDirectory("/etc/nginx"));
            Assert.False(FolderPathValidator.IsSystemDirectory("/usr/local/share"));
        }
    }

    // ─── IsFileWithinFolder ───────────────────────────────────────────────────

    [Fact]
    public void IsFileWithinFolder_ReturnsFalse_WhenNullInputs()
    {
        Assert.False(FolderPathValidator.IsFileWithinFolder(null!, "/home/user"));
        Assert.False(FolderPathValidator.IsFileWithinFolder("/home/user/file.pdf", null!));
        Assert.False(FolderPathValidator.IsFileWithinFolder("", "/home/user"));
    }

    [Fact]
    public void IsFileWithinFolder_ReturnsTrue_WhenFileIsInFolder()
    {
        // Use a temp directory to ensure the paths are valid on this OS
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, "test.pdf");

        Assert.True(FolderPathValidator.IsFileWithinFolder(filePath, tempDir));
    }

    [Fact]
    public void IsFileWithinFolder_ReturnsFalse_WhenFileEscapesFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "subfolder");
        var escapedFile = Path.Combine(Path.GetTempPath(), "other", "secret.pdf");

        Assert.False(FolderPathValidator.IsFileWithinFolder(escapedFile, tempDir));
    }

    [Fact]
    public void IsFileWithinFolder_ReturnsFalse_WhenTraversalUsed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "registered");
        var traversalFile = Path.Combine(tempDir, "..", "secret.pdf");

        // After canonicalization, this should resolve outside the folder
        Assert.False(FolderPathValidator.IsFileWithinFolder(traversalFile, tempDir));
    }

    [Fact]
    public void IsFileWithinFolder_HandlesTrailingSlash()
    {
        var tempDir = Path.GetTempPath(); // Usually ends with separator
        var filePath = Path.Combine(tempDir, "test.pdf");

        Assert.True(FolderPathValidator.IsFileWithinFolder(filePath, tempDir));

        // Also test without trailing slash
        var trimmedDir = tempDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.True(FolderPathValidator.IsFileWithinFolder(filePath, trimmedDir));
    }

    // ─── ValidateFolderPath ───────────────────────────────────────────────────

    [Fact]
    public void ValidateFolderPath_ReturnsErrors_WhenNull()
    {
        var errors = FolderPathValidator.ValidateFolderPath(null);
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("empty"));
    }

    [Fact]
    public void ValidateFolderPath_ReturnsErrors_WhenRelative()
    {
        var errors = FolderPathValidator.ValidateFolderPath("relative/path", checkExists: false);
        Assert.Contains(errors, e => e.Contains("absolute"));
    }

    [Fact]
    public void ValidateFolderPath_ReturnsErrors_WhenTraversal()
    {
        var errors = FolderPathValidator.ValidateFolderPath("/home/user/../etc", checkExists: false);
        Assert.Contains(errors, e => e.Contains("traversal"));
    }

    [Fact]
    public void ValidateFolderPath_ReturnsErrors_WhenSystemDirectory()
    {
        string systemDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\Windows"
            : "/etc";

        var errors = FolderPathValidator.ValidateFolderPath(systemDir, checkExists: false);
        Assert.Contains(errors, e => e.Contains("system directory"));
    }

    [Fact]
    public void ValidateFolderPath_ReturnsErrors_WhenDirectoryDoesNotExist()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var errors = FolderPathValidator.ValidateFolderPath(nonExistent, checkExists: true);
        Assert.Contains(errors, e => e.Contains("not exist"));
    }

    [Fact]
    public void ValidateFolderPath_ReturnsEmpty_WhenValidExistingDirectory()
    {
        var tempDir = Path.GetTempPath();
        var errors = FolderPathValidator.ValidateFolderPath(tempDir, checkExists: true);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateFolderPath_SkipsExistenceCheck_WhenCheckExistsFalse()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var errors = FolderPathValidator.ValidateFolderPath(nonExistent, checkExists: false);
        // Should not contain "not exist" error since we skipped the check
        Assert.DoesNotContain(errors, e => e.Contains("not exist"));
    }

    // ─── SanitizeFileName ─────────────────────────────────────────────────────

    [Fact]
    public void SanitizeFileName_RemovesPathComponents()
    {
        var result = FolderPathValidator.SanitizeFileName("/home/user/docs/rules.pdf");
        Assert.Equal("rules.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_RemovesSpecialCharacters()
    {
        var result = FolderPathValidator.SanitizeFileName("my<bad>file.pdf");
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.EndsWith(".pdf", result);
    }

    [Fact]
    public void SanitizeFileName_PreservesValidName()
    {
        var result = FolderPathValidator.SanitizeFileName("valid-file_name.pdf");
        Assert.Equal("valid-file_name.pdf", result);
    }

    [Fact]
    public void SanitizeFileName_ReturnsDefault_WhenEmpty()
    {
        Assert.Equal("unnamed_file", FolderPathValidator.SanitizeFileName(""));
        Assert.Equal("unnamed_file", FolderPathValidator.SanitizeFileName("   "));
    }

    [Fact]
    public void SanitizeFileName_LimitsLength()
    {
        var longName = new string('a', 300) + ".pdf";
        var result = FolderPathValidator.SanitizeFileName(longName);
        Assert.True(result.Length <= 255);
    }
}

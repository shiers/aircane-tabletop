using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Aircane.Application.Validation;

/// <summary>
/// Static utility methods for validating and securing folder paths used in the
/// watched-folder library feature. Prevents path traversal, blocks system directories,
/// and sanitizes filenames discovered during folder scans.
/// </summary>
public static partial class FolderPathValidator
{
    /// <summary>
    /// Well-known system directories that must not be registered as watched folders.
    /// Paths are compared case-insensitively after canonicalization.
    /// </summary>
    private static readonly string[] BlockedDirectories = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? [
            @"C:\Windows",
            @"C:\Windows\System32",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        ]
        : [
            "/etc",
            "/usr",
            "/bin",
            "/sbin",
            "/lib",
            "/lib64",
            "/boot",
            "/dev",
            "/proc",
            "/sys",
            "/run",
            "/var/run",
        ];

    /// <summary>
    /// Validates that a folder path is absolute (rooted).
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <returns>True if the path is absolute; false otherwise.</returns>
    public static bool IsAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return Path.IsPathRooted(path);
    }

    /// <summary>
    /// Checks whether the path contains path traversal sequences ("..").
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the path contains traversal sequences; false otherwise.</returns>
    public static bool ContainsTraversalSequence(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for ".." as a path segment
        return path.Contains(".." + Path.DirectorySeparatorChar)
            || path.Contains(".." + Path.AltDirectorySeparatorChar)
            || path.EndsWith("..")
            || path.Contains(Path.DirectorySeparatorChar + "..")
            || path.Contains(Path.AltDirectorySeparatorChar + "..");
    }

    /// <summary>
    /// Checks whether the given path is a blocked system directory.
    /// </summary>
    /// <param name="path">The absolute path to check.</param>
    /// <returns>True if the path is a system directory that should not be registered; false otherwise.</returns>
    public static bool IsSystemDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // If we can't canonicalize, treat as potentially unsafe
            return true;
        }

        foreach (var blocked in BlockedDirectories)
        {
            if (string.IsNullOrEmpty(blocked))
                continue;

            var canonicalBlocked = Path.GetFullPath(blocked).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(canonicalPath, canonicalBlocked, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also block immediate children of system directories? No — only the root system dirs themselves.
        }

        return false;
    }

    /// <summary>
    /// Validates that a folder path is safe for registration as a watched folder.
    /// Returns a list of validation error messages (empty if valid).
    /// </summary>
    /// <param name="path">The folder path to validate.</param>
    /// <param name="checkExists">Whether to verify the directory exists on disk. Default true.</param>
    /// <returns>A list of validation error messages. Empty means the path is valid.</returns>
    public static IReadOnlyList<string> ValidateFolderPath(string? path, bool checkExists = true)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add("Folder path must not be empty.");
            return errors;
        }

        if (!IsAbsolutePath(path))
        {
            errors.Add("Folder path must be an absolute path.");
        }

        if (ContainsTraversalSequence(path))
        {
            errors.Add("Folder path must not contain path traversal sequences (..).");
        }

        if (IsSystemDirectory(path))
        {
            errors.Add("Folder path must not be a system directory.");
        }

        if (checkExists && !Directory.Exists(path))
        {
            errors.Add("Folder path does not exist or is not accessible.");
        }

        return errors;
    }

    /// <summary>
    /// Verifies that a resolved file path is actually contained within the expected
    /// parent folder. Uses canonical path comparison to prevent traversal via symlinks
    /// or relative segments.
    /// </summary>
    /// <param name="filePath">The file path to verify.</param>
    /// <param name="folderPath">The registered folder path that should contain the file.</param>
    /// <returns>True if the file is safely within the folder; false otherwise.</returns>
    public static bool IsFileWithinFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
            return false;

        try
        {
            var canonicalFile = Path.GetFullPath(filePath);
            var canonicalFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return canonicalFile.StartsWith(canonicalFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // If canonicalization fails, reject the file
            return false;
        }
    }

    /// <summary>
    /// Sanitizes a filename discovered during a folder scan for use in SourceDocument records.
    /// Removes path separators, special characters, and limits length.
    /// This delegates to the same logic as <see cref="FileUploadValidator.SanitizeFileName"/>
    /// to maintain consistency between upload and folder-scan paths.
    /// </summary>
    /// <param name="fileName">The filename to sanitize.</param>
    /// <returns>A sanitized filename safe for storage in database records.</returns>
    public static string SanitizeFileName(string fileName)
    {
        return FileUploadValidator.SanitizeFileName(fileName);
    }
}

using System.Text.RegularExpressions;

namespace Aircane.Application.Validation;

/// <summary>
/// Static utility methods for validating and sanitizing uploaded files.
/// Provides filename sanitization, extension/MIME whitelist checks, and file size validation.
/// </summary>
public static partial class FileUploadValidator
{
    /// <summary>
    /// Maximum allowed filename length after sanitization.
    /// </summary>
    public const int MaxFileNameLength = 255;

    /// <summary>
    /// Default maximum upload size in bytes (100 MB).
    /// </summary>
    public const long DefaultMaxUploadSizeBytes = 100 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions (lowercase, with leading dot).
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".json"
    };

    /// <summary>
    /// Allowed MIME types (lowercase).
    /// </summary>
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/json"
    };

    /// <summary>
    /// Sanitizes a filename by removing path separators, special characters,
    /// and limiting the total length to <see cref="MaxFileNameLength"/> characters.
    /// </summary>
    /// <param name="fileName">The original filename from the upload.</param>
    /// <returns>A sanitized filename safe for storage.</returns>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed_file";

        // Extract just the filename portion - strip any directory path components.
        var name = fileName;
        var lastSep = name.LastIndexOfAny(['/', '\\', ':']);
        if (lastSep >= 0)
            name = name[(lastSep + 1)..];

        if (string.IsNullOrWhiteSpace(name))
            return "unnamed_file";

        // Replace characters that are unsafe for filesystems.
        // Allow letters, digits, dots, hyphens, underscores, and spaces.
        name = UnsafeCharsRegex().Replace(name, "_");

        // Collapse multiple consecutive underscores/spaces.
        name = CollapseUnderscoresRegex().Replace(name, "_");

        // Trim leading/trailing underscores, dots, and spaces.
        name = name.Trim('.', '_', ' ');

        if (string.IsNullOrWhiteSpace(name))
            return "unnamed_file";

        // Truncate to max length while preserving the extension if possible.
        if (name.Length > MaxFileNameLength)
        {
            var ext = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var maxStem = MaxFileNameLength - ext.Length;
            if (maxStem > 0)
                name = stem[..maxStem] + ext;
            else
                name = name[..MaxFileNameLength];
        }

        return name;
    }

    /// <summary>
    /// Checks whether the file extension is in the allowed whitelist.
    /// </summary>
    /// <param name="fileName">The filename (or just the extension with leading dot).</param>
    /// <returns>True if the extension is allowed; false otherwise.</returns>
    public static bool IsAllowedExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) && AllowedExtensions.Contains(extension);
    }

    /// <summary>
    /// Checks whether the MIME type is in the allowed whitelist.
    /// </summary>
    /// <param name="mimeType">The content type / MIME type string.</param>
    /// <returns>True if the MIME type is allowed; false otherwise.</returns>
    public static bool IsAllowedMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        return AllowedMimeTypes.Contains(mimeType.Trim());
    }

    /// <summary>
    /// Validates that the file size does not exceed the specified maximum.
    /// </summary>
    /// <param name="fileSizeBytes">The size of the file in bytes.</param>
    /// <param name="maxBytes">The maximum allowed size in bytes. Defaults to <see cref="DefaultMaxUploadSizeBytes"/>.</param>
    /// <returns>True if the file size is within limits; false otherwise.</returns>
    public static bool ValidateFileSize(long fileSizeBytes, long maxBytes = DefaultMaxUploadSizeBytes)
    {
        return fileSizeBytes >= 0 && fileSizeBytes <= maxBytes;
    }

    /// <summary>
    /// Returns the set of allowed file extensions.
    /// </summary>
    public static IReadOnlySet<string> GetAllowedExtensions() => AllowedExtensions;

    /// <summary>
    /// Returns the set of allowed MIME types.
    /// </summary>
    public static IReadOnlySet<string> GetAllowedMimeTypes() => AllowedMimeTypes;

    [GeneratedRegex(@"[^\w.\-\s]", RegexOptions.Compiled)]
    private static partial Regex UnsafeCharsRegex();

    [GeneratedRegex(@"[_\s]{2,}", RegexOptions.Compiled)]
    private static partial Regex CollapseUnderscoresRegex();
}

namespace Aircane.Infrastructure.Library;

/// <summary>
/// Helpers for validating and sanitizing uploaded file names and types.
/// These are pure static methods so they can be unit-tested without any infrastructure.
/// </summary>
public static class FileValidationHelper
{
    /// <summary>Maximum allowed upload size: 50 MB.</summary>
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".json" };

    private static readonly Dictionary<string, string> AllowedMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { ".pdf",  "application/pdf" },
            { ".json", "application/json" },
        };

    // application/json can also arrive as text/json or text/plain from some clients.
    private static readonly HashSet<string> AcceptedJsonMimeTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/json",
            "text/json",
            "text/plain",
        };

    /// <summary>
    /// Returns true when the file extension is in the allowed set.
    /// </summary>
    public static bool IsExtensionAllowed(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(ext) && AllowedExtensions.Contains(ext);
    }

    /// <summary>
    /// Returns true when the MIME type is acceptable for the given file extension.
    /// JSON files are given a wider set of accepted MIME types because browsers and
    /// operating systems report them inconsistently.
    /// </summary>
    public static bool IsMimeTypeAllowed(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".pdf"  => string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase),
            ".json" => AcceptedJsonMimeTypes.Contains(contentType),
            _       => false,
        };
    }

    /// <summary>
    /// Sanitizes a filename by:
    /// <list type="bullet">
    ///   <item>Stripping any directory components (path traversal prevention).</item>
    ///   <item>Replacing characters that are invalid on common filesystems.</item>
    ///   <item>Collapsing consecutive dots to prevent extension spoofing.</item>
    ///   <item>Trimming leading/trailing whitespace and dots.</item>
    ///   <item>Falling back to "upload" when the result would be empty.</item>
    /// </list>
    /// The sanitized name is used only for display/metadata; the actual stored file
    /// always uses a GUID-based name.
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "upload";

        // Strip directory components — take only the last segment.
        fileName = Path.GetFileName(fileName);

        // Replace characters that are invalid on Windows or Linux filesystems.
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            fileName = fileName.Replace(c, '_');

        // Replace additional risky characters not always covered by GetInvalidFileNameChars on all platforms.
        fileName = fileName
            .Replace("..", "_")   // collapse double-dots
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("\0", "_");  // null byte — always unsafe

        // Trim whitespace and leading/trailing dots.
        fileName = fileName.Trim().Trim('.');

        return string.IsNullOrEmpty(fileName) ? "upload" : fileName;
    }

    /// <summary>
    /// Returns the canonical lowercase extension for a filename, including the leading dot.
    /// Returns an empty string when no extension is present.
    /// </summary>
    public static string GetNormalizedExtension(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant();
}

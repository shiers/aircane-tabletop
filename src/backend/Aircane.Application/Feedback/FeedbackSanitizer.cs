using System.Net;
using System.Text.RegularExpressions;

namespace Aircane.Application.Feedback;

/// <summary>
/// Sanitizes tester-supplied free text before it is embedded in a GitHub issue. Strips HTML
/// tags, decodes HTML entities, and caps the length. Used for the <c>Summary</c> and
/// <c>Description</c> fields.
/// </summary>
public static partial class FeedbackSanitizer
{
    /// <summary>Default maximum length for a sanitized field.</summary>
    public const int DefaultMaxLength = 2000;

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    /// <summary>
    /// Removes HTML tags, decodes HTML entities, and truncates to <paramref name="maxLength"/>.
    /// Returns an empty string for null/whitespace input.
    /// </summary>
    public static string Sanitize(string? input, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Strip HTML tags, then decode entities so e.g. "&lt;b&gt;" does not re-introduce markup.
        var stripped = HtmlTagRegex().Replace(input, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);

        // Decoding may re-create tag-like sequences (e.g. "&lt;script&gt;"); strip once more.
        decoded = HtmlTagRegex().Replace(decoded, string.Empty).Trim();

        if (maxLength > 0 && decoded.Length > maxLength)
            decoded = decoded[..maxLength];

        return decoded;
    }
}

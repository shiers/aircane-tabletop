using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Aircane.Application.Validation;

/// <summary>
/// Validates a document thumbnail key. The artwork manifest lives in the frontend, so the backend only
/// checks the shape of the key (lowercase words joined by single hyphens, up to 64 characters), not
/// that an image exists for it. A key whose image was later removed is handled by the frontend.
/// </summary>
public static partial class ThumbnailKeyValidator
{
    /// <summary>Maximum key length (matches the database column).</summary>
    public const int MaxLength = 64;

    /// <summary>Message returned for an invalid key.</summary>
    public const string InvalidMessage =
        "Thumbnail key must be 1-64 characters: lowercase letters and digits, with single hyphens between words (for example 'fantasy-knight-pf'), or empty for automatic.";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>
    /// Normalises and validates a key. Null, empty and whitespace-only values mean "automatic" and
    /// become null. Returns false (with <paramref name="error"/>) for anything else that is invalid.
    /// </summary>
    public static bool TryNormalize(string? input, out string? key, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            key = null;
            error = null;
            return true;
        }

        if (input.Length > MaxLength || !KeyPattern().IsMatch(input))
        {
            key = null;
            error = InvalidMessage;
            return false;
        }

        key = input;
        error = null;
        return true;
    }

    /// <summary>True when <paramref name="input"/> is null/empty (automatic) or a well-formed key.</summary>
    public static bool IsValid(string? input) => TryNormalize(input, out _, out _);
}

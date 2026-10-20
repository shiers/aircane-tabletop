using System.Security.Cryptography;
using System.Text;

namespace Aircane.Infrastructure.Sessions;

/// <summary>
/// The rejoin secret: a random value a player's browser makes once per session and sends with its join
/// request, so it can take its seat back later without a new request. Only its SHA-256 is stored. The secret
/// has at least 128 bits of randomness, so an unsalted hash is enough: there is nothing to guess.
/// </summary>
public static class RejoinSecret
{
    /// <summary>At least 128 bits.</summary>
    public const int MinBytes = 16;

    /// <summary>A generous upper bound, so a request cannot make the server hash megabytes.</summary>
    public const int MaxBytes = 64;

    /// <summary>
    /// The hash to store or compare, or null when <paramref name="secret"/> is not base64url (no padding) of
    /// <see cref="MinBytes"/> to <see cref="MaxBytes"/> bytes.
    /// </summary>
    public static string? TryHash(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > (MaxBytes * 4 + 2) / 3)
            return null;

        var bytes = TryDecodeBase64Url(secret);
        if (bytes is null || bytes.Length < MinBytes || bytes.Length > MaxBytes)
            return null;

        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
    }

    /// <summary>Compares two stored-form hashes in constant time.</summary>
    public static bool Matches(string? storedHash, string candidateHash)
    {
        if (storedHash is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(storedHash), Encoding.ASCII.GetBytes(candidateHash));
    }

    private static byte[]? TryDecodeBase64Url(string text)
    {
        foreach (var c in text)
        {
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
                return null;
        }

        var base64 = text.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 1: return null;
        }

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

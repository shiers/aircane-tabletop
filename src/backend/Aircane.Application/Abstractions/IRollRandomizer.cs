namespace Aircane.Application.Abstractions;

/// <summary>
/// Abstraction over die-roll random number generation.
/// The default implementation uses <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
/// for cryptographically random results. Tests inject a deterministic implementation.
/// </summary>
public interface IRollRandomizer
{
    /// <summary>
    /// Returns a random integer in the range [1, <paramref name="sides"/>] (inclusive).
    /// </summary>
    int RollDie(int sides);
}

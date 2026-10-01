namespace Aircane.Domain.DiceExpressions;

/// <summary>
/// Abstraction for random number generation, enabling deterministic testing of dice rolls.
/// </summary>
public interface IRandomSource
{
    /// <summary>
    /// Returns a random integer in the range [minInclusive, maxExclusive).
    /// </summary>
    int Next(int minInclusive, int maxExclusive);
}

/// <summary>
/// Default random source using System.Random for production use.
/// </summary>
public class DefaultRandomSource : IRandomSource
{
    private readonly Random _random = new();

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}

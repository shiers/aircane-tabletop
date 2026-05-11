using System.Security.Cryptography;
using Aircane.Application.Abstractions;

namespace Aircane.Infrastructure.Dice;

/// <summary>
/// Cryptographically random die roller backed by
/// <see cref="RandomNumberGenerator"/>.
/// </summary>
public sealed class CryptoRollRandomizer : IRollRandomizer
{
    /// <inheritdoc />
    public int RollDie(int sides)
    {
        if (sides < 2)
            throw new ArgumentOutOfRangeException(nameof(sides), "A die must have at least 2 sides.");

        // Use rejection sampling to avoid modulo bias.
        // The range [0, sides) is mapped to [1, sides].
        return RandomNumberGenerator.GetInt32(1, sides + 1);
    }
}

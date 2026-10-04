using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Picks the source mapper for an uploaded character JSON by consulting each registered
/// <see cref="ICharacterSourceMapper"/> in ascending <see cref="ICharacterSourceMapper.Order"/>.
/// Detection order is owned by <c>Order</c>, not DI registration order, so the generic fallback
/// (<see cref="int.MaxValue"/>) is always the last resort.
/// </summary>
public sealed class CharacterFormatDetector
{
    private readonly IReadOnlyList<ICharacterSourceMapper> _ordered;

    public CharacterFormatDetector(IEnumerable<ICharacterSourceMapper> mappers)
    {
        _ordered = mappers.OrderBy(m => m.Order).ToList();
    }

    /// <summary>
    /// Returns the first mapper whose <see cref="ICharacterSourceMapper.CanMap"/> accepts the
    /// document, or <see cref="CharacterImportSource.Unknown"/> with a null mapper if none match.
    /// </summary>
    public (CharacterImportSource Source, ICharacterSourceMapper? Mapper) Detect(JsonDocument doc)
    {
        foreach (var mapper in _ordered)
        {
            if (SafeCanMap(mapper, doc))
                return (mapper.Source, mapper);
        }

        return (CharacterImportSource.Unknown, null);
    }

    private static bool SafeCanMap(ICharacterSourceMapper mapper, JsonDocument doc)
    {
        // Untrusted JSON must never crash detection.
        try
        {
            return mapper.CanMap(doc);
        }
        catch
        {
            return false;
        }
    }
}

using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a single external character-sheet JSON format into a <see cref="SourceMapResult"/>.
/// Implementations treat all input as untrusted: neither <see cref="CanMap"/> nor
/// <see cref="Map"/> may throw on missing, null, or wrong-typed JSON.
/// </summary>
public interface ICharacterSourceMapper
{
    /// <summary>The source format this mapper recognises.</summary>
    CharacterImportSource Source { get; }

    /// <summary>
    /// Detection precedence. Lower runs first. The generic fallback uses <see cref="int.MaxValue"/>
    /// so it is always the last resort regardless of DI registration order; specific mappers use 0.
    /// </summary>
    int Order => 0;

    /// <summary>Cheap structural check: does this JSON look like my source format? Never throws.</summary>
    bool CanMap(JsonDocument doc);

    /// <summary>
    /// Builds a <see cref="SourceMapResult"/> from the source JSON. Never throws on missing/null/
    /// wrong-type input: unresolved values are left at their <see cref="CanonicalCharacter"/>
    /// defaults and surfaced via <see cref="SourceMapResult.ExtraFields"/> /
    /// <see cref="SourceMapResult.RequiresReviewPaths"/>.
    /// </summary>
    SourceMapResult Map(JsonDocument doc);
}

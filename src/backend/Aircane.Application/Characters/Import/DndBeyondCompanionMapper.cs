using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a D&amp;D Beyond "Companion"/DDB-Importer export, which wraps the character payload under a
/// <c>character</c> envelope alongside a <c>_meta</c> block (DDB-Importer) or a <c>ddbId</c>
/// (Companion). Unwraps <c>root.character</c> and delegates to
/// <see cref="DndBeyondApiMapper.MapFromRoot"/> so both share one field-mapping code path. Forces
/// the D&amp;D 5e 2014 game system. Parses defensively and never throws.
/// </summary>
public sealed class DndBeyondCompanionMapper : ICharacterSourceMapper
{
    public CharacterImportSource Source => CharacterImportSource.DndBeyondCompanion;

    public int Order => 0;

    /// <summary>
    /// Companion export: root has a <c>character</c> object carrying <c>name</c> + <c>classes</c>,
    /// AND the root carries a <c>_meta</c> object OR a <c>ddbId</c> field. Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("character", out var character) ||
            character.ValueKind != JsonValueKind.Object)
            return false;

        if (!character.TryGetProperty("name", out _) || !character.TryGetProperty("classes", out _))
            return false;

        return root.TryGetProperty("_meta", out _) || root.TryGetProperty("ddbId", out _);
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("character", out var character) &&
            character.ValueKind == JsonValueKind.Object)
        {
            return DndBeyondApiMapper.MapFromRoot(character);
        }

        // Defensive fallback: nothing usable to unwrap. Return an empty, forced-system result.
        return DndBeyondApiMapper.MapFromRoot(default);
    }
}

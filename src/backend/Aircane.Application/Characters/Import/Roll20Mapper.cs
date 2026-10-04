using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a Roll20 character export (<c>schema_version</c> + <c>character.attribs</c> flat array)
/// into a <see cref="SourceMapResult"/>. Best-effort: it reads a D&amp;D 5e Roll20 sheet by attrib
/// name, flags EVERY mapped value for review, and never forces a game system (so the controller
/// runs the game-system-selection handshake). Parses defensively and never throws.
/// </summary>
public sealed class Roll20Mapper : ICharacterSourceMapper
{
    public CharacterImportSource Source => CharacterImportSource.Roll20;

    public int Order => 0;

    /// <summary>
    /// Roll20 export: root has <c>schema_version</c> AND <c>root.character</c> carries an
    /// <c>attribs</c> array. Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("schema_version", out _))
            return false;

        if (!root.TryGetProperty("character", out var character) ||
            character.ValueKind != JsonValueKind.Object)
            return false;

        return character.TryGetProperty("attribs", out var attribs) &&
               attribs.ValueKind == JsonValueKind.Array;
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresReview = new List<string>();

        var attribs = ReadAttribs(doc.RootElement);

        // ── Identity ──────────────────────────────────────────────────────────
        if (TryGetAttrib(attribs, "character_name", out var name))
        {
            character.Identity.Name = name;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.IdentityName, name);
        }

        if (TryGetAttrib(attribs, "race", out var race))
        {
            character.Identity.RaceOrAncestry = race;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.IdentityRaceOrAncestry, race);
        }

        // ── Class / level ───────────────────────────────────────────────────────
        var className = TryGetAttrib(attribs, "class", out var cls) ? cls : null;
        var hasLevel = TryGetAttribInt(attribs, "level", out var level);

        if (className is not null || hasLevel)
        {
            var entry = new CharacterClass
            {
                ClassName = className ?? string.Empty,
                Level = hasLevel ? level : 0,
                HitDie = 8,
            };
            character.Classes.Add(entry);

            if (className is not null)
                AddMapped(mapped, requiresReview, CanonicalCharacterPaths.Class, className);
            if (hasLevel)
                AddMapped(mapped, requiresReview, CanonicalCharacterPaths.Level,
                    level.ToString(CultureInfo.InvariantCulture));
        }

        // ── Abilities ───────────────────────────────────────────────────────────
        MapAbilityAttrib(attribs, "strength", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityStrength, v => character.Abilities.Strength = v);
        MapAbilityAttrib(attribs, "dexterity", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityDexterity, v => character.Abilities.Dexterity = v);
        MapAbilityAttrib(attribs, "constitution", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityConstitution, v => character.Abilities.Constitution = v);
        MapAbilityAttrib(attribs, "intelligence", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityIntelligence, v => character.Abilities.Intelligence = v);
        MapAbilityAttrib(attribs, "wisdom", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityWisdom, v => character.Abilities.Wisdom = v);
        MapAbilityAttrib(attribs, "charisma", mapped, requiresReview,
            CanonicalCharacterPaths.AbilityCharisma, v => character.Abilities.Charisma = v);

        // ── Combat ────────────────────────────────────────────────────────────────
        if (TryGetAttribInt(attribs, "hp", out var hp))
        {
            character.Combat.CurrentHitPoints = hp;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.CombatHitPoints,
                hp.ToString(CultureInfo.InvariantCulture));
        }

        if (TryGetAttribInt(attribs, "hp_max", out var hpMax))
        {
            character.Combat.MaxHitPoints = hpMax;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.CombatMaxHitPoints,
                hpMax.ToString(CultureInfo.InvariantCulture));
        }

        if (TryGetAttribInt(attribs, "ac", out var ac))
        {
            character.Combat.ArmorClass = ac;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.CombatArmorClass,
                ac.ToString(CultureInfo.InvariantCulture));
        }

        if (TryGetAttribInt(attribs, "speed", out var speed))
        {
            character.Combat.Speed = speed;
            AddMapped(mapped, requiresReview, CanonicalCharacterPaths.CombatSpeed,
                speed.ToString(CultureInfo.InvariantCulture));
        }

        return new SourceMapResult
        {
            Character = character,
            MappedFields = mapped,
            ExtraFields = extra,
            RequiresReviewPaths = requiresReview,
            Confidence = ImportConfidence.Low,
            GameSystemIdentifier = null,
        };
    }

    /// <summary>
    /// Collapses the flat <c>attribs</c> array (each entry <c>{name,current,max}</c>) into a
    /// case-insensitive lookup of attrib name → current value string.
    /// </summary>
    private static Dictionary<string, string> ReadAttribs(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("character", out var character) ||
            character.ValueKind != JsonValueKind.Object ||
            !character.TryGetProperty("attribs", out var attribs) ||
            attribs.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var attrib in attribs.EnumerateArray())
        {
            if (attrib.ValueKind != JsonValueKind.Object)
                continue;

            if (!attrib.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;

            var attribName = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(attribName) || result.ContainsKey(attribName))
                continue;

            if (!attrib.TryGetProperty("current", out var currentEl))
                continue;

            var current = currentEl.ValueKind switch
            {
                JsonValueKind.String => currentEl.GetString(),
                JsonValueKind.Number => currentEl.GetRawText(),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(current))
                result[attribName] = current;
        }

        return result;
    }

    private static void MapAbilityAttrib(
        IReadOnlyDictionary<string, string> attribs,
        string attribName,
        IDictionary<string, string> mapped,
        ICollection<string> requiresReview,
        string path,
        Action<int> set)
    {
        if (TryGetAttribInt(attribs, attribName, out var value))
        {
            set(value);
            AddMapped(mapped, requiresReview, path, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AddMapped(
        IDictionary<string, string> mapped,
        ICollection<string> requiresReview,
        string path,
        string value)
    {
        mapped[path] = value;
        requiresReview.Add(path);
    }

    private static bool TryGetAttrib(IReadOnlyDictionary<string, string> attribs, string name, out string value)
    {
        if (attribs.TryGetValue(name, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetAttribInt(IReadOnlyDictionary<string, string> attribs, string name, out int value)
    {
        value = 0;
        return attribs.TryGetValue(name, out var raw) &&
               int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

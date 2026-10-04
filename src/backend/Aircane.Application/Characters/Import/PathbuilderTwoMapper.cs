using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a Pathbuilder 2e export (root <c>build</c> object) into a <see cref="SourceMapResult"/>.
/// Forces the Pathfinder 2e Remaster game system. Parses defensively: missing, null, or
/// wrong-typed fields are skipped, never thrown on.
/// </summary>
public sealed class PathbuilderTwoMapper : ICharacterSourceMapper
{
    public CharacterImportSource Source => CharacterImportSource.PathbuilderTwo;

    public int Order => 0;

    /// <summary>
    /// Pathbuilder export: root has <c>build</c>; <c>build</c> has <c>class</c> AND <c>ancestry</c>;
    /// root has NO <c>system</c> (which would indicate a Foundry actor). Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("system", out _))
            return false;

        if (!root.TryGetProperty("build", out var build) || build.ValueKind != JsonValueKind.Object)
            return false;

        return build.TryGetProperty("class", out _) && build.TryGetProperty("ancestry", out _);
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresReview = new List<string>();

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("build", out var build) ||
            build.ValueKind != JsonValueKind.Object)
        {
            return BuildResult(character, mapped, extra, requiresReview);
        }

        // ── Identity ──────────────────────────────────────────────────────────
        if (TryGetString(build, "name", out var name))
        {
            character.Identity.Name = name;
            mapped[CanonicalCharacterPaths.IdentityName] = name;
        }

        if (TryGetString(build, "ancestry", out var ancestry))
        {
            character.Identity.RaceOrAncestry = ancestry;
            mapped[CanonicalCharacterPaths.IdentityRaceOrAncestry] = ancestry;
        }

        if (TryGetString(build, "background", out var background))
        {
            character.Identity.Background = background;
            mapped[CanonicalCharacterPaths.IdentityBackground] = background;
        }

        // ── Class / level ───────────────────────────────────────────────────────
        var className = TryGetString(build, "class", out var cls) ? cls : null;
        var hasLevel = TryGetInt(build, "level", out var level);

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
                mapped[CanonicalCharacterPaths.Class] = className;
            if (hasLevel)
                mapped[CanonicalCharacterPaths.Level] = level.ToString(CultureInfo.InvariantCulture);
        }

        // ── Abilities ───────────────────────────────────────────────────────────
        if (build.TryGetProperty("abilities", out var abilities) &&
            abilities.ValueKind == JsonValueKind.Object)
        {
            MapAbility(abilities, "str", mapped, CanonicalCharacterPaths.AbilityStrength,
                v => character.Abilities.Strength = v);
            MapAbility(abilities, "dex", mapped, CanonicalCharacterPaths.AbilityDexterity,
                v => character.Abilities.Dexterity = v);
            MapAbility(abilities, "con", mapped, CanonicalCharacterPaths.AbilityConstitution,
                v => character.Abilities.Constitution = v);
            MapAbility(abilities, "int", mapped, CanonicalCharacterPaths.AbilityIntelligence,
                v => character.Abilities.Intelligence = v);
            MapAbility(abilities, "wis", mapped, CanonicalCharacterPaths.AbilityWisdom,
                v => character.Abilities.Wisdom = v);
            MapAbility(abilities, "cha", mapped, CanonicalCharacterPaths.AbilityCharisma,
                v => character.Abilities.Charisma = v);
        }

        // ── Combat (attributes) ──────────────────────────────────────────────────
        if (build.TryGetProperty("attributes", out var attributes) &&
            attributes.ValueKind == JsonValueKind.Object)
        {
            if (TryGetInt(attributes, "hp", out var hp))
            {
                // Pathbuilder exports a max HP only; current HP is DERIVED (= max).
                character.Combat.MaxHitPoints = hp;
                character.Combat.CurrentHitPoints = hp;
                var hpStr = hp.ToString(CultureInfo.InvariantCulture);
                mapped[CanonicalCharacterPaths.CombatMaxHitPoints] = hpStr;
                mapped[CanonicalCharacterPaths.CombatHitPoints] = hpStr;
                requiresReview.Add(CanonicalCharacterPaths.CombatHitPoints);
            }

            if (TryGetInt(attributes, "speed", out var speed))
            {
                character.Combat.Speed = speed;
                mapped[CanonicalCharacterPaths.CombatSpeed] = speed.ToString(CultureInfo.InvariantCulture);
            }

            if (TryGetInt(attributes, "ac", out var ac))
            {
                character.Combat.ArmorClass = ac;
                mapped[CanonicalCharacterPaths.CombatArmorClass] = ac.ToString(CultureInfo.InvariantCulture);
            }

            if (TryGetInt(attributes, "classDC", out var classDc))
                extra["Class DC"] = classDc.ToString(CultureInfo.InvariantCulture);
        }

        // ── Extra fields (no ApplyMapping path) ─────────────────────────────────
        if (TryGetString(build, "heritage", out var heritage))
            extra["Heritage"] = heritage;

        AddSaveOrSkill(build, "perception", "Perception", level, extra);
        AddSaveOrSkill(build, "fortitude", "Fortitude", level, extra);
        AddSaveOrSkill(build, "reflex", "Reflex", level, extra);
        AddSaveOrSkill(build, "will", "Will", level, extra);

        AddLores(build, level, extra);
        AddFeats(build, extra);
        AddSpellCasters(build, extra);
        AddEquipment(build, extra);

        return BuildResult(character, mapped, extra, requiresReview);
    }

    private static SourceMapResult BuildResult(
        CanonicalCharacter character,
        IReadOnlyDictionary<string, string> mapped,
        IReadOnlyDictionary<string, string> extra,
        IReadOnlyCollection<string> requiresReview) =>
        new()
        {
            Character = character,
            MappedFields = mapped,
            ExtraFields = extra,
            RequiresReviewPaths = requiresReview,
            Confidence = ImportConfidence.High,
            Ruleset = "Remaster",
            GameSystemIdentifier = "pathfinder-2e-remaster",
        };

    private static void MapAbility(
        JsonElement abilities,
        string key,
        IDictionary<string, string> mapped,
        string path,
        Action<int> set)
    {
        if (TryGetInt(abilities, key, out var value))
        {
            set(value);
            mapped[path] = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Pathbuilder stores proficiency ranks (0/2/4/6/8 style) for saves/perception; convert to a
    /// bonus via <c>rank*2 + level</c> only when the rank is a usable positive proficiency.
    /// </summary>
    private static void AddSaveOrSkill(
        JsonElement build,
        string key,
        string label,
        int level,
        IDictionary<string, string> extra)
    {
        if (!TryGetInt(build, key, out var rank))
            return;

        extra[label] = FormatBonus(RankToBonus(rank, level));
    }

    private static void AddLores(JsonElement build, int level, IDictionary<string, string> extra)
    {
        if (!build.TryGetProperty("lores", out var lores) || lores.ValueKind != JsonValueKind.Array)
            return;

        foreach (var lore in lores.EnumerateArray())
        {
            // Pathbuilder encodes each lore as a two-element array: [name, rank].
            if (lore.ValueKind != JsonValueKind.Array)
                continue;

            string? loreName = null;
            var rank = 0;
            var index = 0;
            foreach (var element in lore.EnumerateArray())
            {
                if (index == 0 && element.ValueKind == JsonValueKind.String)
                    loreName = element.GetString();
                else if (index == 1 && element.ValueKind == JsonValueKind.Number &&
                         element.TryGetInt32(out var r))
                    rank = r;
                index++;
            }

            if (!string.IsNullOrWhiteSpace(loreName))
                extra[$"{loreName} Lore"] = FormatBonus(RankToBonus(rank, level));
        }
    }

    private static void AddFeats(JsonElement build, IDictionary<string, string> extra)
    {
        if (!build.TryGetProperty("feats", out var feats) || feats.ValueKind != JsonValueKind.Array)
            return;

        var names = new List<string>();
        foreach (var feat in feats.EnumerateArray())
        {
            // Each feat is typically an array whose first element is the feat name.
            if (feat.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in feat.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var featName = element.GetString();
                        if (!string.IsNullOrWhiteSpace(featName))
                            names.Add(featName);
                        break;
                    }
                }
            }
            else if (feat.ValueKind == JsonValueKind.String)
            {
                var featName = feat.GetString();
                if (!string.IsNullOrWhiteSpace(featName))
                    names.Add(featName);
            }
        }

        if (names.Count > 0)
            extra["Feats"] = string.Join(", ", names);
    }

    private static void AddSpellCasters(JsonElement build, IDictionary<string, string> extra)
    {
        if (!build.TryGetProperty("spellCasters", out var casters) ||
            casters.ValueKind != JsonValueKind.Array)
            return;

        var summaries = new List<string>();
        foreach (var caster in casters.EnumerateArray())
        {
            if (caster.ValueKind != JsonValueKind.Object)
                continue;

            var parts = new List<string>();
            if (TryGetString(caster, "name", out var casterName))
                parts.Add(casterName);
            if (TryGetString(caster, "magicTradition", out var tradition))
                parts.Add(tradition);

            if (parts.Count > 0)
                summaries.Add(string.Join(" ", parts));
        }

        if (summaries.Count > 0)
            extra["Spellcasting"] = string.Join("; ", summaries);
    }

    private static void AddEquipment(JsonElement build, IDictionary<string, string> extra)
    {
        if (!build.TryGetProperty("equipment", out var equipment) ||
            equipment.ValueKind != JsonValueKind.Array)
            return;

        var items = new List<string>();
        foreach (var item in equipment.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in item.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var itemName = element.GetString();
                        if (!string.IsNullOrWhiteSpace(itemName))
                            items.Add(itemName);
                        break;
                    }
                }
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                var itemName = item.GetString();
                if (!string.IsNullOrWhiteSpace(itemName))
                    items.Add(itemName);
            }
        }

        if (items.Count > 0)
            extra["Equipment"] = string.Join(", ", items);
    }

    /// <summary>Converts a PF2e proficiency rank (0/1/2/3/4) to a bonus of <c>rank*2 + level</c>.</summary>
    private static int RankToBonus(int rank, int level)
    {
        if (rank <= 0)
            return 0;
        return (rank * 2) + Math.Max(0, level);
    }

    private static string FormatBonus(int bonus) =>
        bonus >= 0
            ? "+" + bonus.ToString(CultureInfo.InvariantCulture)
            : bonus.ToString(CultureInfo.InvariantCulture);

    private static bool TryGetString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        var s = element.GetString();
        if (string.IsNullOrWhiteSpace(s))
            return false;

        value = s;
        return true;
    }

    private static bool TryGetInt(JsonElement parent, string name, out int value)
    {
        value = 0;
        if (!parent.TryGetProperty(name, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            value = number;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}

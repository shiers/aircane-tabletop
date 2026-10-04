using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a Foundry VTT <c>pf2e</c> actor export into a <see cref="SourceMapResult"/>.
/// Forces the Pathfinder 2e Remaster game system. Parses defensively: missing, null, or
/// wrong-typed fields are skipped, never thrown on.
/// </summary>
public sealed class FoundryPf2eMapper : ICharacterSourceMapper
{
    public CharacterImportSource Source => CharacterImportSource.FoundryPf2e;

    public int Order => 0;

    /// <summary>
    /// Foundry pf2e actor: <c>root.type=="character"</c> AND <c>system.details.ancestry</c> is
    /// present (which a dnd5e actor never carries). Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "character", StringComparison.Ordinal))
            return false;

        if (!root.TryGetProperty("system", out var system) || system.ValueKind != JsonValueKind.Object)
            return false;

        if (!system.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Object)
            return false;

        return details.TryGetProperty("ancestry", out _);
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return BuildResult(character, mapped, extra);

        // ── Identity ──────────────────────────────────────────────────────────
        if (TryGetString(root, "name", out var name))
        {
            character.Identity.Name = name;
            mapped[CanonicalCharacterPaths.IdentityName] = name;
        }

        root.TryGetProperty("system", out var system);
        var hasSystem = system.ValueKind == JsonValueKind.Object;

        JsonElement details = default;
        var hasDetails = hasSystem && system.TryGetProperty("details", out details) &&
                         details.ValueKind == JsonValueKind.Object;

        if (hasDetails)
        {
            if (TryGetNestedName(details, "ancestry", out var ancestry))
            {
                character.Identity.RaceOrAncestry = ancestry;
                mapped[CanonicalCharacterPaths.IdentityRaceOrAncestry] = ancestry;
            }

            if (TryGetNestedName(details, "heritage", out var heritage))
                extra["Heritage"] = heritage;

            if (TryGetNestedName(details, "background", out var background))
            {
                character.Identity.Background = background;
                mapped[CanonicalCharacterPaths.IdentityBackground] = background;
            }

            // ── Class / level ──────────────────────────────────────────────────
            var className = TryGetNestedName(details, "class", out var cls) ? cls : null;

            var hasLevel = false;
            var level = 0;
            if (details.TryGetProperty("level", out var levelEl) &&
                levelEl.ValueKind == JsonValueKind.Object &&
                TryGetInt(levelEl, "value", out var levelValue))
            {
                level = levelValue;
                hasLevel = true;
            }

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
        }

        // ── Abilities (system.abilities.<key>.value — PF2e stores modifiers) ─────
        if (hasSystem && system.TryGetProperty("abilities", out var abilities) &&
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

        // ── Combat (system.attributes) ───────────────────────────────────────────
        if (hasSystem && system.TryGetProperty("attributes", out var attributes) &&
            attributes.ValueKind == JsonValueKind.Object)
        {
            if (attributes.TryGetProperty("hp", out var hp) && hp.ValueKind == JsonValueKind.Object)
            {
                if (TryGetInt(hp, "value", out var hpValue))
                {
                    character.Combat.CurrentHitPoints = hpValue;
                    mapped[CanonicalCharacterPaths.CombatHitPoints] =
                        hpValue.ToString(CultureInfo.InvariantCulture);
                }

                if (TryGetInt(hp, "max", out var hpMax))
                {
                    character.Combat.MaxHitPoints = hpMax;
                    mapped[CanonicalCharacterPaths.CombatMaxHitPoints] =
                        hpMax.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (attributes.TryGetProperty("ac", out var ac) && ac.ValueKind == JsonValueKind.Object &&
                TryGetInt(ac, "value", out var acValue))
            {
                character.Combat.ArmorClass = acValue;
                mapped[CanonicalCharacterPaths.CombatArmorClass] =
                    acValue.ToString(CultureInfo.InvariantCulture);
            }

            if (attributes.TryGetProperty("speed", out var speed) && speed.ValueKind == JsonValueKind.Object &&
                TryGetInt(speed, "value", out var speedValue))
            {
                character.Combat.Speed = speedValue;
                mapped[CanonicalCharacterPaths.CombatSpeed] =
                    speedValue.ToString(CultureInfo.InvariantCulture);
            }

            // Saves + class DC → ExtraFields (no ApplyMapping path).
            AddSaveValue(attributes, "classDC", "Class DC", extra);
            if (attributes.TryGetProperty("saves", out var saves) && saves.ValueKind == JsonValueKind.Object)
            {
                AddSaveValue(saves, "fortitude", "Fortitude", extra);
                AddSaveValue(saves, "reflex", "Reflex", extra);
                AddSaveValue(saves, "will", "Will", extra);
            }
        }

        // ── Extra fields: items ───────────────────────────────────────────────────
        AddItemSummaries(root, extra);

        return BuildResult(character, mapped, extra);
    }

    private static SourceMapResult BuildResult(
        CanonicalCharacter character,
        IReadOnlyDictionary<string, string> mapped,
        IReadOnlyDictionary<string, string> extra) =>
        new()
        {
            Character = character,
            MappedFields = mapped,
            ExtraFields = extra,
            Confidence = ImportConfidence.High,
            Ruleset = "Remaster",
            GameSystemIdentifier = "pathfinder-2e-remaster",
        };

    private static void AddItemSummaries(JsonElement root, IDictionary<string, string> extra)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["action"] = [],
            ["feat"] = [],
            ["spell"] = [],
            ["weapon"] = [],
            ["armor"] = [],
            ["equipment"] = [],
        };

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryGetString(item, "type", out var type) || !buckets.ContainsKey(type))
                continue;

            if (TryGetString(item, "name", out var itemName))
                buckets[type].Add(itemName);
        }

        AppendBucket(extra, "Actions", buckets["action"]);
        AppendBucket(extra, "Feats", buckets["feat"]);
        AppendBucket(extra, "Spells", buckets["spell"]);
        AppendBucket(extra, "Weapons", buckets["weapon"]);
        AppendBucket(extra, "Armor", buckets["armor"]);
        AppendBucket(extra, "Equipment", buckets["equipment"]);
    }

    private static void AppendBucket(IDictionary<string, string> extra, string label, List<string> names)
    {
        if (names.Count > 0)
            extra[label] = string.Join(", ", names);
    }

    private static void AddSaveValue(
        JsonElement parent,
        string key,
        string label,
        IDictionary<string, string> extra)
    {
        if (!parent.TryGetProperty(key, out var save))
            return;

        if (save.ValueKind == JsonValueKind.Object && TryGetInt(save, "value", out var value))
            extra[label] = FormatBonus(value);
        else if (save.ValueKind == JsonValueKind.Number && save.TryGetInt32(out var number))
            extra[label] = FormatBonus(number);
    }

    private static void MapAbility(
        JsonElement abilities,
        string key,
        IDictionary<string, string> mapped,
        string path,
        Action<int> set)
    {
        if (abilities.TryGetProperty(key, out var ability) && ability.ValueKind == JsonValueKind.Object &&
            TryGetInt(ability, "value", out var value))
        {
            set(value);
            mapped[path] = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Reads <c>parent.<paramref name="name"/>.name</c> as a non-empty string.</summary>
    private static bool TryGetNestedName(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(name, out var child) || child.ValueKind != JsonValueKind.Object)
            return false;

        return TryGetString(child, "name", out value);
    }

    private static string FormatBonus(int bonus) =>
        bonus >= 0
            ? "+" + bonus.ToString(CultureInfo.InvariantCulture)
            : bonus.ToString(CultureInfo.InvariantCulture);

    private static bool TryGetString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
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
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var element))
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

using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a Foundry VTT <c>dnd5e</c> actor export into a <see cref="SourceMapResult"/>.
/// Forces the D&amp;D 5e 2014 game system. Parses defensively: missing, null, or wrong-typed
/// fields are skipped, never thrown on.
/// </summary>
public sealed class FoundryDnd5eMapper : ICharacterSourceMapper
{
    /// <summary>Foundry dnd5e skill keys (18) surfaced in ExtraFields when a total is present.</summary>
    private static readonly string[] SkillKeys =
    [
        "acr", "ani", "arc", "ath", "dec", "his", "ins", "itm", "inv",
        "med", "nat", "prc", "prf", "per", "rel", "slt", "ste", "sur"
    ];

    /// <summary>Publications that imply the 2024 ("One D&amp;D") ruleset in the source book field.</summary>
    private static readonly string[] Ruleset2024Books =
    [
        "Player's Handbook (2024)", "PHB 2024", "PHB2024", "2024"
    ];

    public CharacterImportSource Source => CharacterImportSource.FoundryDnd5e;

    public int Order => 0;

    /// <summary>
    /// Foundry dnd5e actor: <c>root.type=="character"</c> AND <c>root.system</c> has
    /// <c>abilities</c> AND <c>system.attributes.hp</c> exists AND it is NOT a pf2e actor
    /// (<c>system.details.ancestry</c> absent). Never throws.
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

        if (!system.TryGetProperty("abilities", out var abilities) ||
            abilities.ValueKind != JsonValueKind.Object)
            return false;

        if (!system.TryGetProperty("attributes", out var attributes) ||
            attributes.ValueKind != JsonValueKind.Object ||
            !attributes.TryGetProperty("hp", out _))
            return false;

        // Exclude pf2e actors, which carry system.details.ancestry.
        if (system.TryGetProperty("details", out var details) &&
            details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("ancestry", out _))
            return false;

        return true;
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return BuildResult(character, mapped, extra, "2014", true);

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
            if (TryGetString(details, "race", out var race))
            {
                character.Identity.RaceOrAncestry = race;
                mapped[CanonicalCharacterPaths.IdentityRaceOrAncestry] = race;
            }

            if (TryGetString(details, "subtype", out var subrace))
                extra["Subrace"] = subrace;

            if (TryGetString(details, "background", out var background))
            {
                character.Identity.Background = background;
                mapped[CanonicalCharacterPaths.IdentityBackground] = background;
            }
        }

        // ── Class / level ───────────────────────────────────────────────────────
        var className = ReadItemName(root, "class");
        var subclass = ReadItemName(root, "subclass");

        // Level: prefer system.details.level, else sum of class item levels.
        var hasLevel = false;
        var level = 0;
        if (hasDetails && TryGetInt(details, "level", out var detailLevel))
        {
            level = detailLevel;
            hasLevel = true;
        }
        else
        {
            var summed = SumClassLevels(root);
            if (summed > 0)
            {
                level = summed;
                hasLevel = true;
            }
        }

        if (className is not null || subclass is not null || hasLevel)
        {
            var entry = new CharacterClass
            {
                ClassName = className ?? string.Empty,
                Level = hasLevel ? level : 0,
                Subclass = subclass,
                HitDie = 8,
            };
            character.Classes.Add(entry);

            if (className is not null)
                mapped[CanonicalCharacterPaths.Class] = className;
            if (hasLevel)
                mapped[CanonicalCharacterPaths.Level] = level.ToString(CultureInfo.InvariantCulture);
        }

        // ── Abilities (system.abilities.<key>.value) ────────────────────────────
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

                if (TryGetInt(hp, "temp", out var hpTemp))
                    character.Combat.TemporaryHitPoints = hpTemp;
            }

            if (attributes.TryGetProperty("ac", out var ac) && ac.ValueKind == JsonValueKind.Object &&
                TryGetInt(ac, "value", out var acValue))
            {
                character.Combat.ArmorClass = acValue;
                mapped[CanonicalCharacterPaths.CombatArmorClass] =
                    acValue.ToString(CultureInfo.InvariantCulture);
            }

            if (attributes.TryGetProperty("movement", out var movement) &&
                movement.ValueKind == JsonValueKind.Object &&
                TryGetInt(movement, "walk", out var walk))
            {
                character.Combat.Speed = walk;
                mapped[CanonicalCharacterPaths.CombatSpeed] = walk.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ── Extra fields: items + skill totals ───────────────────────────────────
        AddItemSummaries(root, extra);
        AddSkillTotals(system, hasSystem, extra);

        var (ruleset, requiresConfirmation) = DetectRuleset(root);
        return BuildResult(character, mapped, extra, ruleset, requiresConfirmation);
    }

    /// <summary>
    /// Ruleset detection: primary signal is <c>root._stats.systemVersion</c>
    /// (<see cref="Version"/> &gt;= 3.0.0 ⇒ "2024", else "2014"); fallback is
    /// <c>system.details.source.book</c> referencing a known 2024 publication; when neither signal
    /// is present it defaults to "2014" and flags <see cref="SourceMapResult.RulesetRequiresConfirmation"/>.
    /// </summary>
    private static (string Ruleset, bool RequiresConfirmation) DetectRuleset(JsonElement root)
    {
        if (root.TryGetProperty("_stats", out var stats) && stats.ValueKind == JsonValueKind.Object &&
            TryGetString(stats, "systemVersion", out var systemVersion) &&
            Version.TryParse(systemVersion, out var version))
        {
            return version >= new Version(3, 0, 0) ? ("2024", false) : ("2014", false);
        }

        if (root.TryGetProperty("system", out var system) && system.ValueKind == JsonValueKind.Object &&
            system.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object &&
            TryGetString(source, "book", out var book))
        {
            foreach (var marker in Ruleset2024Books)
            {
                if (book.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return ("2024", false);
            }

            return ("2014", false);
        }

        return ("2014", true);
    }

    private static int SumClassLevels(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return 0;

        var total = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryGetString(item, "type", out var type) ||
                !string.Equals(type, "class", StringComparison.Ordinal))
                continue;

            if (item.TryGetProperty("system", out var itemSystem) &&
                itemSystem.ValueKind == JsonValueKind.Object &&
                TryGetInt(itemSystem, "levels", out var levels))
                total += levels;
        }

        return total;
    }

    /// <summary>Returns the <c>name</c> of the first <c>items[]</c> entry whose <c>type</c> matches.</summary>
    private static string? ReadItemName(JsonElement root, string itemType)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (TryGetString(item, "type", out var type) &&
                string.Equals(type, itemType, StringComparison.Ordinal) &&
                TryGetString(item, "name", out var name))
                return name;
        }

        return null;
    }

    private static void AddItemSummaries(JsonElement root, IDictionary<string, string> extra)
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return;

        var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["spell"] = [],
            ["feat"] = [],
            ["weapon"] = [],
            ["equipment"] = [],
            ["tool"] = [],
            ["consumable"] = [],
            ["loot"] = [],
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

        AppendBucket(extra, "Spells", buckets["spell"]);
        AppendBucket(extra, "Features", buckets["feat"]);
        AppendBucket(extra, "Weapons", buckets["weapon"]);
        AppendBucket(extra, "Equipment", buckets["equipment"]);
        AppendBucket(extra, "Tools", buckets["tool"]);
        AppendBucket(extra, "Consumables", buckets["consumable"]);
        AppendBucket(extra, "Loot", buckets["loot"]);
    }

    private static void AppendBucket(IDictionary<string, string> extra, string label, List<string> names)
    {
        if (names.Count > 0)
            extra[label] = string.Join(", ", names);
    }

    private static void AddSkillTotals(JsonElement system, bool hasSystem, IDictionary<string, string> extra)
    {
        if (!hasSystem || !system.TryGetProperty("skills", out var skills) ||
            skills.ValueKind != JsonValueKind.Object)
            return;

        foreach (var key in SkillKeys)
        {
            if (skills.TryGetProperty(key, out var skill) && skill.ValueKind == JsonValueKind.Object &&
                TryGetInt(skill, "total", out var total))
                extra[$"Skill {key}"] = FormatBonus(total);
        }
    }

    private static SourceMapResult BuildResult(
        CanonicalCharacter character,
        IReadOnlyDictionary<string, string> mapped,
        IReadOnlyDictionary<string, string> extra,
        string ruleset,
        bool rulesetRequiresConfirmation) =>
        new()
        {
            Character = character,
            MappedFields = mapped,
            ExtraFields = extra,
            Confidence = ImportConfidence.High,
            Ruleset = ruleset,
            RulesetRequiresConfirmation = rulesetRequiresConfirmation,
            GameSystemIdentifier = "dnd-5e-2014",
        };

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

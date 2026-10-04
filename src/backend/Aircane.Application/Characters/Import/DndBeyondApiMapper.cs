using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps a D&amp;D Beyond character-service API JSON document (the shape returned by
/// <c>character-service.dndbeyond.com/character/v5/character/{id}</c>, saved to a file) into a
/// <see cref="SourceMapResult"/>. Forces the D&amp;D 5e 2014 game system and always flags the
/// detected ruleset for user confirmation. Parses defensively: missing, null, or wrong-typed fields
/// are skipped, never thrown on.
/// </summary>
public sealed class DndBeyondApiMapper : ICharacterSourceMapper
{
    public CharacterImportSource Source => CharacterImportSource.DndBeyondApi;

    public int Order => 0;

    /// <summary>
    /// D&amp;D Beyond API export: root is an object carrying <c>id</c>, <c>dateModified</c>,
    /// <c>classes</c>, <c>stats</c>, and <c>race</c>. Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc) => CanMapRoot(doc.RootElement);

    /// <summary>
    /// Structural detection against an already-resolved root element (reused by the Companion
    /// mapper which first unwraps <c>root.character</c>). Never throws.
    /// </summary>
    internal static bool CanMapRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        return root.TryGetProperty("id", out _) &&
               root.TryGetProperty("dateModified", out _) &&
               root.TryGetProperty("classes", out _) &&
               root.TryGetProperty("stats", out _) &&
               root.TryGetProperty("race", out _);
    }

    public SourceMapResult Map(JsonDocument doc) => MapFromRoot(doc.RootElement);

    /// <summary>
    /// Builds a <see cref="SourceMapResult"/> from a D&amp;D Beyond character root element. Exposed
    /// so <see cref="DndBeyondCompanionMapper"/> can unwrap its <c>character</c> envelope and reuse
    /// the exact same field logic. Never throws.
    /// </summary>
    internal static SourceMapResult MapFromRoot(JsonElement root)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresReview = new List<string>();

        if (root.ValueKind != JsonValueKind.Object)
            return BuildResult(character, mapped, extra, requiresReview, "2014", true);

        // ── Identity ──────────────────────────────────────────────────────────
        if (TryGetString(root, "name", out var name))
        {
            character.Identity.Name = name;
            mapped[CanonicalCharacterPaths.IdentityName] = name;
        }

        if (root.TryGetProperty("race", out var race) && race.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(race, "fullName", out var raceName))
            {
                character.Identity.RaceOrAncestry = raceName;
                mapped[CanonicalCharacterPaths.IdentityRaceOrAncestry] = raceName;
            }

            if (TryGetString(race, "subRaceShortName", out var subrace))
                extra["Subrace"] = subrace;
        }

        // ── Class / level ───────────────────────────────────────────────────────
        MapClasses(root, character, mapped, extra);

        // ── Abilities (base + all bonus layers) ─────────────────────────────────
        MapAbilities(root, character, mapped, requiresReview);

        // ── Combat ───────────────────────────────────────────────────────────────
        MapHitPoints(root, character, mapped);
        MapArmorClass(root, character, mapped);

        // ── Extra fields (no ApplyMapping path) ─────────────────────────────────
        AddSpells(root, extra);
        AddInventory(root, extra);

        var ruleset = DetectRuleset(root);
        return BuildResult(character, mapped, extra, requiresReview, ruleset, true);
    }

    /// <summary>
    /// Determines the ruleset by scanning <c>classes[].definition.sources[].sourceId</c>: any id in
    /// <see cref="DndBeyondSources.Ruleset2024SourceIds"/> yields "2024", otherwise "2014". The
    /// result always requires user confirmation. Never throws.
    /// </summary>
    internal static string DetectRuleset(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("classes", out var classes) ||
            classes.ValueKind != JsonValueKind.Array)
            return "2014";

        foreach (var cls in classes.EnumerateArray())
        {
            if (cls.ValueKind != JsonValueKind.Object ||
                !cls.TryGetProperty("definition", out var definition) ||
                definition.ValueKind != JsonValueKind.Object ||
                !definition.TryGetProperty("sources", out var sources) ||
                sources.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var source in sources.EnumerateArray())
            {
                if (source.ValueKind == JsonValueKind.Object &&
                    TryGetInt(source, "sourceId", out var sourceId) &&
                    DndBeyondSources.Ruleset2024SourceIds.Contains(sourceId))
                    return "2024";
            }
        }

        return "2014";
    }

    private static SourceMapResult BuildResult(
        CanonicalCharacter character,
        IReadOnlyDictionary<string, string> mapped,
        IReadOnlyDictionary<string, string> extra,
        IReadOnlyCollection<string> requiresReview,
        string ruleset,
        bool rulesetRequiresConfirmation) =>
        new()
        {
            Character = character,
            MappedFields = mapped,
            ExtraFields = extra,
            RequiresReviewPaths = requiresReview,
            Confidence = ImportConfidence.High,
            Ruleset = ruleset,
            RulesetRequiresConfirmation = rulesetRequiresConfirmation,
            GameSystemIdentifier = "dnd-5e-2014",
        };

    private static void MapClasses(
        JsonElement root,
        CanonicalCharacter character,
        IDictionary<string, string> mapped,
        IDictionary<string, string> extra)
    {
        if (!root.TryGetProperty("classes", out var classes) || classes.ValueKind != JsonValueKind.Array)
            return;

        var totalLevel = 0;
        string? firstClassName = null;
        string? firstSubclass = null;
        var sawClass = false;

        foreach (var cls in classes.EnumerateArray())
        {
            if (cls.ValueKind != JsonValueKind.Object)
                continue;

            sawClass = true;

            if (TryGetInt(cls, "level", out var level))
                totalLevel += level;

            if (firstClassName is null &&
                cls.TryGetProperty("definition", out var definition) &&
                definition.ValueKind == JsonValueKind.Object &&
                TryGetString(definition, "name", out var className))
            {
                firstClassName = className;
            }

            if (firstSubclass is null &&
                cls.TryGetProperty("subclassDefinition", out var subclassDef) &&
                subclassDef.ValueKind == JsonValueKind.Object &&
                TryGetString(subclassDef, "name", out var subclassName))
            {
                firstSubclass = subclassName;
            }
        }

        if (!sawClass)
            return;

        var entry = new CharacterClass
        {
            ClassName = firstClassName ?? string.Empty,
            Level = totalLevel,
            Subclass = firstSubclass,
            HitDie = 8,
        };
        character.Classes.Add(entry);

        if (firstClassName is not null)
            mapped[CanonicalCharacterPaths.Class] = firstClassName;

        if (totalLevel > 0)
            mapped[CanonicalCharacterPaths.Level] = totalLevel.ToString(CultureInfo.InvariantCulture);

        if (firstSubclass is not null)
            extra["Subclass"] = firstSubclass;
    }

    /// <summary>
    /// Computes each ability score as <c>stats[i].value</c> plus every bonus layer D&amp;D Beyond
    /// records (<c>bonusStats</c>, <c>overrideStats</c>, and racial/feat/item modifiers in
    /// <c>modifiers</c>). An override, when present, replaces the base+bonus total. A summed score
    /// outside 1..30 is kept but flagged for review.
    /// </summary>
    private static void MapAbilities(
        JsonElement root,
        CanonicalCharacter character,
        IDictionary<string, string> mapped,
        ICollection<string> requiresReview)
    {
        // D&D Beyond ability ordering: 1=STR, 2=DEX, 3=CON, 4=INT, 5=WIS, 6=CHA (stat id).
        var baseStats = ReadStatArray(root, "stats");
        var bonusStats = ReadStatArray(root, "bonusStats");
        var overrideStats = ReadStatArray(root, "overrideStats");
        var modifierBonuses = ReadModifierAbilityBonuses(root);

        Apply(1, CanonicalCharacterPaths.AbilityStrength, v => character.Abilities.Strength = v);
        Apply(2, CanonicalCharacterPaths.AbilityDexterity, v => character.Abilities.Dexterity = v);
        Apply(3, CanonicalCharacterPaths.AbilityConstitution, v => character.Abilities.Constitution = v);
        Apply(4, CanonicalCharacterPaths.AbilityIntelligence, v => character.Abilities.Intelligence = v);
        Apply(5, CanonicalCharacterPaths.AbilityWisdom, v => character.Abilities.Wisdom = v);
        Apply(6, CanonicalCharacterPaths.AbilityCharisma, v => character.Abilities.Charisma = v);

        void Apply(int statId, string path, Action<int> set)
        {
            if (!baseStats.TryGetValue(statId, out var baseValue))
                return;

            int score;
            if (overrideStats.TryGetValue(statId, out var overrideValue))
            {
                score = overrideValue;
            }
            else
            {
                score = baseValue;
                if (bonusStats.TryGetValue(statId, out var bonus))
                    score += bonus;
                if (modifierBonuses.TryGetValue(statId, out var modBonus))
                    score += modBonus;
            }

            set(score);
            mapped[path] = score.ToString(CultureInfo.InvariantCulture);

            if (score < 1 || score > 30)
                requiresReview.Add(path);
        }
    }

    /// <summary>
    /// Reads a D&amp;D Beyond stat array (<c>[{ "id": 1, "value": 15 }, …]</c>) into a stat-id → value
    /// map. Entries with a null/absent value are skipped.
    /// </summary>
    private static Dictionary<int, int> ReadStatArray(JsonElement root, string propertyName)
    {
        var result = new Dictionary<int, int>();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var stat in array.EnumerateArray())
        {
            if (stat.ValueKind != JsonValueKind.Object || !TryGetInt(stat, "id", out var id))
                continue;

            if (stat.TryGetProperty("value", out var valueEl) &&
                valueEl.ValueKind == JsonValueKind.Number &&
                valueEl.TryGetInt32(out var value))
            {
                result[id] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Sums ability-score bonuses recorded in the <c>modifiers</c> object (keyed by category such as
    /// <c>race</c>, <c>class</c>, <c>feat</c>, <c>item</c>). A bonus modifier is recognised by its
    /// <c>type == "bonus"</c> and a <c>subType</c> ending in "-score" (e.g. "strength-score"), with
    /// an integer <c>value</c>. Returns a stat-id → total-bonus map.
    /// </summary>
    private static Dictionary<int, int> ReadModifierAbilityBonuses(JsonElement root)
    {
        var result = new Dictionary<int, int>();
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("modifiers", out var modifiers) ||
            modifiers.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var category in modifiers.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var modifier in category.Value.EnumerateArray())
            {
                if (modifier.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryGetString(modifier, "type", out var type) ||
                    !string.Equals(type, "bonus", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryGetString(modifier, "subType", out var subType))
                    continue;

                var statId = AbilityStatIdForSubType(subType);
                if (statId == 0)
                    continue;

                if (modifier.TryGetProperty("value", out var valueEl) &&
                    valueEl.ValueKind == JsonValueKind.Number &&
                    valueEl.TryGetInt32(out var value))
                {
                    result[statId] = result.GetValueOrDefault(statId) + value;
                }
            }
        }

        return result;
    }

    private static int AbilityStatIdForSubType(string subType) =>
        subType.ToLowerInvariant() switch
        {
            "strength-score" => 1,
            "dexterity-score" => 2,
            "constitution-score" => 3,
            "intelligence-score" => 4,
            "wisdom-score" => 5,
            "charisma-score" => 6,
            _ => 0,
        };

    private static void MapHitPoints(
        JsonElement root,
        CanonicalCharacter character,
        IDictionary<string, string> mapped)
    {
        // hitPointInfo lives directly on the root in the v5 shape; some exports nest the fields on
        // the root itself (baseHitPoints, removedHitPoints, temporaryHitPoints).
        var hpSource = root;
        if (root.TryGetProperty("hitPointInfo", out var info) && info.ValueKind == JsonValueKind.Object)
            hpSource = info;

        var hasBase = TryGetInt(hpSource, "baseHitPoints", out var baseHp);
        if (!hasBase)
            return;

        // Constitution contribution: con modifier * total level, when both are known.
        var conBonus = ComputeConHitPointBonus(root, character);

        // Floor the derived values so the mapper never emits a negative HP that the validator would
        // hard-reject: max at 0, current clamped to 0..max, temp at 0. The shared draft sanitizer
        // enforces the same floors as a safety net, but flooring at the source keeps the mapped
        // string values consistent with the stored numbers.
        var max = Math.Max(0, baseHp + conBonus);
        character.Combat.MaxHitPoints = max;
        mapped[CanonicalCharacterPaths.CombatMaxHitPoints] = max.ToString(CultureInfo.InvariantCulture);

        var removed = TryGetInt(hpSource, "removedHitPoints", out var rem) ? rem : 0;
        var current = Math.Max(0, Math.Min(max, max - removed));
        character.Combat.CurrentHitPoints = current;
        mapped[CanonicalCharacterPaths.CombatHitPoints] = current.ToString(CultureInfo.InvariantCulture);

        if (TryGetInt(hpSource, "temporaryHitPoints", out var temp))
            character.Combat.TemporaryHitPoints = Math.Max(0, temp);
    }

    private static int ComputeConHitPointBonus(JsonElement root, CanonicalCharacter character)
    {
        var totalLevel = character.Classes.Sum(c => c.Level);
        if (totalLevel <= 0)
            return 0;

        var conModifier = AbilityScores.ModifierFor(character.Abilities.Constitution);
        return conModifier * totalLevel;
    }

    private static void MapArmorClass(
        JsonElement root,
        CanonicalCharacter character,
        IDictionary<string, string> mapped)
    {
        int? ac = null;

        if (root.TryGetProperty("armorClass", out var armor) && armor.ValueKind == JsonValueKind.Object)
        {
            if (TryGetInt(armor, "totalArmorClass", out var total))
                ac = total;
        }

        if (TryGetInt(root, "overrideArmorClass", out var overrideAc))
            ac = overrideAc;

        if (ac is null)
            return;

        character.Combat.ArmorClass = ac.Value;
        mapped[CanonicalCharacterPaths.CombatArmorClass] = ac.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddSpells(JsonElement root, IDictionary<string, string> extra)
    {
        if (!root.TryGetProperty("spells", out var spells))
            return;

        var names = new List<string>();

        // DDB nests spells by caster (class/race/item). Walk any array of {definition:{name}} or
        // {name} shaped objects we encounter one level deep.
        if (spells.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in spells.EnumerateObject())
                CollectSpellNames(group.Value, names);
        }
        else if (spells.ValueKind == JsonValueKind.Array)
        {
            CollectSpellNames(spells, names);
        }

        if (names.Count > 0)
            extra["Spells"] = string.Join(", ", names);
    }

    private static void CollectSpellNames(JsonElement element, List<string> names)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return;

        foreach (var spell in element.EnumerateArray())
        {
            if (spell.ValueKind != JsonValueKind.Object)
                continue;

            string? spellName = null;
            if (spell.TryGetProperty("definition", out var definition) &&
                definition.ValueKind == JsonValueKind.Object &&
                TryGetString(definition, "name", out var defName))
                spellName = defName;
            else if (TryGetString(spell, "name", out var directName))
                spellName = directName;

            if (!string.IsNullOrWhiteSpace(spellName))
                names.Add(spellName);
        }
    }

    private static void AddInventory(JsonElement root, IDictionary<string, string> extra)
    {
        if (!root.TryGetProperty("inventory", out var inventory) ||
            inventory.ValueKind != JsonValueKind.Array)
            return;

        var items = new List<string>();
        foreach (var item in inventory.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (item.TryGetProperty("definition", out var definition) &&
                definition.ValueKind == JsonValueKind.Object &&
                TryGetString(definition, "name", out var itemName))
                items.Add(itemName);
            else if (TryGetString(item, "name", out var directName))
                items.Add(directName);
        }

        if (items.Count > 0)
            extra["Inventory"] = string.Join(", ", items);
    }

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

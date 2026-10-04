using System.Globalization;
using System.Text.Json;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Last-resort best-effort mapper for an unidentified VTT/character JSON. Uses loose heuristics to
/// pull a name and whatever ability scores it can find, flags EVERYTHING for review, never forces a
/// game system, and attaches a warning telling the user to review all fields. Parses defensively
/// and never throws. Runs last (<see cref="Order"/> = <see cref="int.MaxValue"/>).
/// </summary>
public sealed class GenericVttMapper : ICharacterSourceMapper
{
    private const string UnidentifiedWarning =
        "We couldn't identify this character sheet format. Please review and correct all fields before saving.";

    /// <summary>Property names that plausibly carry the character's name.</summary>
    private static readonly string[] NameKeys = ["name", "character_name", "charname", "Name"];

    /// <summary>
    /// Candidate property spellings per ability, searched case-insensitively anywhere in the
    /// document (top-level or nested under containers like <c>abilities</c>/<c>stats</c>).
    /// </summary>
    private static readonly (string Path, Action<CanonicalCharacter, int> Set, string[] Keys)[] AbilitySpecs =
    [
        (CanonicalCharacterPaths.AbilityStrength, (c, v) => c.Abilities.Strength = v,
            ["str", "strength"]),
        (CanonicalCharacterPaths.AbilityDexterity, (c, v) => c.Abilities.Dexterity = v,
            ["dex", "dexterity"]),
        (CanonicalCharacterPaths.AbilityConstitution, (c, v) => c.Abilities.Constitution = v,
            ["con", "constitution"]),
        (CanonicalCharacterPaths.AbilityIntelligence, (c, v) => c.Abilities.Intelligence = v,
            ["int", "intelligence"]),
        (CanonicalCharacterPaths.AbilityWisdom, (c, v) => c.Abilities.Wisdom = v,
            ["wis", "wisdom"]),
        (CanonicalCharacterPaths.AbilityCharisma, (c, v) => c.Abilities.Charisma = v,
            ["cha", "charisma"]),
    ];

    /// <summary>Containers under which ability scores commonly nest.</summary>
    private static readonly string[] AbilityContainers = ["abilities", "stats", "attributes", "scores"];

    public CharacterImportSource Source => CharacterImportSource.GenericVtt;

    public int Order => int.MaxValue;

    /// <summary>
    /// Matches when the document carries a plausible name OR any ability-score heuristic. Never throws.
    /// </summary>
    public bool CanMap(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (TryFindName(root, out _))
            return true;

        foreach (var spec in AbilitySpecs)
        {
            if (TryFindAbility(root, spec.Keys, out _))
                return true;
        }

        return false;
    }

    public SourceMapResult Map(JsonDocument doc)
    {
        var character = new CanonicalCharacter();
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var requiresReview = new List<string>();

        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryFindName(root, out var name))
            {
                character.Identity.Name = name;
                mapped[CanonicalCharacterPaths.IdentityName] = name;
                requiresReview.Add(CanonicalCharacterPaths.IdentityName);
            }

            foreach (var spec in AbilitySpecs)
            {
                if (TryFindAbility(root, spec.Keys, out var value))
                {
                    spec.Set(character, value);
                    mapped[spec.Path] = value.ToString(CultureInfo.InvariantCulture);
                    requiresReview.Add(spec.Path);
                }
            }
        }

        return new SourceMapResult
        {
            Character = character,
            MappedFields = mapped,
            RequiresReviewPaths = requiresReview,
            Confidence = ImportConfidence.Low,
            GameSystemIdentifier = null,
            Warnings = [UnidentifiedWarning],
        };
    }

    private static bool TryFindName(JsonElement root, out string value)
    {
        foreach (var key in NameKeys)
        {
            if (TryGetStringCaseInsensitive(root, key, out value))
                return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Looks for an ability score at the top level and under common container objects, matching the
    /// candidate keys case-insensitively. A nested value may itself be a number or an object with a
    /// <c>value</c>/<c>score</c>/<c>current</c> member.
    /// </summary>
    private static bool TryFindAbility(JsonElement root, string[] keys, out int value)
    {
        foreach (var key in keys)
        {
            if (TryGetAbilityValue(root, key, out value))
                return true;
        }

        foreach (var container in AbilityContainers)
        {
            if (TryGetPropertyCaseInsensitive(root, container, out var child) &&
                child.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in keys)
                {
                    if (TryGetAbilityValue(child, key, out value))
                        return true;
                }
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetAbilityValue(JsonElement parent, string key, out int value)
    {
        value = 0;
        if (!TryGetPropertyCaseInsensitive(parent, key, out var element))
            return false;

        if (TryReadInt(element, out value))
            return true;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var inner in new[] { "value", "score", "current" })
            {
                if (TryGetPropertyCaseInsensitive(element, inner, out var innerEl) &&
                    TryReadInt(innerEl, out value))
                    return true;
            }
        }

        return false;
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        value = 0;
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

    private static bool TryGetStringCaseInsensitive(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetPropertyCaseInsensitive(parent, name, out var element) ||
            element.ValueKind != JsonValueKind.String)
            return false;

        var s = element.GetString();
        if (string.IsNullOrWhiteSpace(s))
            return false;

        value = s;
        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object)
            return false;

        // Fast path: exact match.
        if (parent.TryGetProperty(name, out value))
            return true;

        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

using System.Text.RegularExpressions;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// The single implementation of the D&amp;D Beyond "class &amp; level" split (e.g. <c>"Fighter 5"</c>
/// → class <c>Fighter</c>, level <c>5</c>; <c>"Wizard 3 / Rogue 2"</c> → two classes). Used by the
/// generic PDF class/level mapping (<c>PdfCharacterExtractor</c>) so there is exactly one split rule.
/// </summary>
public static partial class DndBeyondClassLevelSplit
{
    private static readonly Regex SingleClassLevel = BuildSingleClassLevelRegex();

    /// <summary>
    /// Parses <paramref name="value"/> and appends the resulting <see cref="CharacterClass"/>
    /// entries to <paramref name="character"/>. Handles multiclass "A 5 / B 3" notation and the
    /// simple "Name N" shape; a class name with no level is added at level 1. Appends a human
    /// warning to <paramref name="warnings"/> for unparseable or level-less input. Never throws.
    /// </summary>
    public static void Apply(string value, CanonicalCharacter character, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(warnings);

        if (string.IsNullOrWhiteSpace(value))
            return;

        // Multiclass notation is delegated to the per-entry parser.
        if (value.Contains('/'))
        {
            ApplyMulticlass(value, character, warnings);
            return;
        }

        var match = SingleClassLevel.Match(value.Trim());
        if (match.Success && int.TryParse(match.Groups[2].Value, out var level))
        {
            var className = match.Groups[1].Value.Trim();
            character.Classes.Add(new CharacterClass
            {
                ClassName = className,
                Level = level,
                HitDie = DefaultHitDieForClass(className),
            });
            return;
        }

        // Fall back to the general per-entry parser (handles name-only, extra spacing, etc.).
        ApplyMulticlass(value, character, warnings);
    }

    private static void ApplyMulticlass(string value, CanonicalCharacter character, List<string> warnings)
    {
        var entries = value.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var parts = entry.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && int.TryParse(parts[^1], out var level))
            {
                var className = string.Join(" ", parts[..^1]);
                character.Classes.Add(new CharacterClass
                {
                    ClassName = className,
                    Level = level,
                    HitDie = DefaultHitDieForClass(className),
                });
            }
            else if (parts.Length == 1)
            {
                character.Classes.Add(new CharacterClass
                {
                    ClassName = parts[0],
                    Level = 1,
                    HitDie = DefaultHitDieForClass(parts[0]),
                });
                warnings.Add($"'ClassLevel' value '{value}' did not include a level - defaulting to 1.");
            }
            else
            {
                warnings.Add($"'ClassLevel' value '{value}' could not be parsed.");
            }
        }
    }

    /// <summary>Returns a sensible default hit die for well-known D&amp;D 5e class names (d8 fallback).</summary>
    public static int DefaultHitDieForClass(string className) =>
        className.Trim().ToLowerInvariant() switch
        {
            "barbarian" => 12,
            "fighter" or "paladin" or "ranger" => 10,
            "bard" or "cleric" or "druid" or "monk" or "rogue" or "warlock" => 8,
            "sorcerer" or "wizard" => 6,
            _ => 8,
        };

    [GeneratedRegex(@"^(.+?)\s+(\d+)$")]
    private static partial Regex BuildSingleClassLevelRegex();
}

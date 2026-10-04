using Aircane.Application.Abstractions;

namespace Aircane.Application.Characters.Import;

/// <summary>
/// Maps OCR'd D&amp;D Beyond printable-sheet value regions to a <see cref="CanonicalCharacter"/>,
/// using the one caption vocabulary (<see cref="DndBeyondPdfHints.CaptionMap"/>) and the single
/// class/level split (<see cref="DndBeyondClassLevelSplit"/>).
/// <para>
/// <b>Default-and-flag (never guess).</b> The canonical ability/combat fields are non-nullable
/// <c>int</c> with hard defaults (abilities 10, AC 10, Speed 30, Prof Bonus 2, HP 0). For a NUMERIC
/// caption the mapper PRE-VALIDATES the OCR text with the same strip + <c>int.TryParse</c> the
/// AcroForm path uses; on success it writes the parsed value, on failure it SKIPS the field (leaving
/// the canonical default untouched, emitting NO "defaulting to N" warning) and flags the path for
/// review. Text captions apply as-is (empty ⇒ skip + flag). EVERY caption the mapper handles is
/// added to <see cref="PdfCharacterExtractionResult.RequiresReviewPaths"/> (FR-4.3). Unparseable
/// text is never routed through a canonical apply that would stamp a default as if parsed.
/// </para>
/// </summary>
public static class DndBeyondOcrMapper
{
    /// <summary>
    /// Produces a <see cref="PdfCharacterExtractionResult"/> from OCR'd regions. The result's
    /// <see cref="PdfCharacterExtractionResult.MappedCharacter"/> carries the applied values, its
    /// <see cref="PdfCharacterExtractionResult.RequiresReviewPaths"/> lists every OCR-mapped canonical
    /// path, and <see cref="PdfCharacterExtractionResult.DetectedRuleset"/> carries the ruleset
    /// (flagged requires-confirmation by the controller).
    /// </summary>
    public static PdfCharacterExtractionResult Map(IReadOnlyList<RegionOcrResult> regions, DdbRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(regions);

        var character = new CanonicalCharacter();
        var warnings = new List<string>();
        var reviewPaths = new List<string>();
        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in regions)
        {
            var caption = region.Key?.Trim() ?? string.Empty;
            if (caption.Length == 0)
                continue;

            var text = region.Text?.Trim() ?? string.Empty;
            extracted[caption] = text;

            // CLASS & LEVEL is special: split into class name + level via the shared helper. It is
            // flagged for review via the class/level paths. Empty text ⇒ flag only.
            if (string.Equals(caption, DndBeyondPdfHints.ClassLevelCaption, StringComparison.OrdinalIgnoreCase))
            {
                if (text.Length > 0)
                    DndBeyondClassLevelSplit.Apply(text, character, warnings);

                AddReviewPath(reviewPaths, CanonicalCharacterPaths.Class);
                AddReviewPath(reviewPaths, CanonicalCharacterPaths.Level);
                continue;
            }

            if (!DndBeyondPdfHints.CaptionMap.TryGetValue(caption, out var canonicalPath))
                continue; // Caption with no canonical path (e.g. a stray signature token) → ignore.

            // Every OCR-mapped field is flagged for review, whether or not its value parsed (FR-4.3).
            AddReviewPath(reviewPaths, canonicalPath);

            if (IsNumericPath(canonicalPath))
            {
                // Pre-validate with the SAME parse the canonical apply uses. On failure, skip the
                // field (leave the canonical default) — never route garbage through an apply that
                // would stamp the default and emit a misleading "defaulting to N" warning.
                if (TryParseNumeric(canonicalPath, text, out var parsed))
                    ApplyNumeric(canonicalPath, parsed, character);
                // else: default-and-flag (already flagged above; no warning).
            }
            else
            {
                // Text captions apply as-is; empty ⇒ skip (already flagged above).
                if (text.Length > 0)
                    ApplyText(canonicalPath, text, character);
            }
        }

        return new PdfCharacterExtractionResult
        {
            ExtractedFields = extracted,
            MappedCharacter = character,
            UnmappedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            IsOcrRequired = false,
            Warnings = warnings,
            RequiresReviewPaths = reviewPaths,
            DetectedRuleset = ruleset,
        };
    }

    private static void AddReviewPath(List<string> reviewPaths, string path)
    {
        if (!reviewPaths.Contains(path, StringComparer.Ordinal))
            reviewPaths.Add(path);
    }

    private static bool IsNumericPath(string canonicalPath) => canonicalPath switch
    {
        CanonicalCharacterPaths.AbilityStrength => true,
        CanonicalCharacterPaths.AbilityDexterity => true,
        CanonicalCharacterPaths.AbilityConstitution => true,
        CanonicalCharacterPaths.AbilityIntelligence => true,
        CanonicalCharacterPaths.AbilityWisdom => true,
        CanonicalCharacterPaths.AbilityCharisma => true,
        CanonicalCharacterPaths.CombatArmorClass => true,
        CanonicalCharacterPaths.CombatMaxHitPoints => true,
        CanonicalCharacterPaths.CombatHitPoints => true,
        CanonicalCharacterPaths.CombatSpeed => true,
        CanonicalCharacterPaths.CombatProficiencyBonus => true,
        _ => false,
    };

    /// <summary>
    /// Mirrors the AcroForm path's parse rules exactly: ability scores strip a modifier suffix
    /// ("16 (+3)" → 16); other ints strip a unit suffix ("30 ft." → 30). Returns false (⇒ skip)
    /// when the stripped text is not an integer. No value is fabricated.
    /// </summary>
    private static bool TryParseNumeric(string canonicalPath, string value, out int result)
    {
        var isAbility = canonicalPath is
            CanonicalCharacterPaths.AbilityStrength or
            CanonicalCharacterPaths.AbilityDexterity or
            CanonicalCharacterPaths.AbilityConstitution or
            CanonicalCharacterPaths.AbilityIntelligence or
            CanonicalCharacterPaths.AbilityWisdom or
            CanonicalCharacterPaths.AbilityCharisma;

        var cleaned = isAbility
            ? value.Split([' ', '('])[0].Trim()                 // ParseAbilityScore strip
            : value.Split(' ')[0].Trim().TrimEnd('f', 't', '.'); // ParseIntField strip

        return int.TryParse(cleaned, out result);
    }

    private static void ApplyNumeric(string canonicalPath, int value, CanonicalCharacter character)
    {
        switch (canonicalPath)
        {
            case CanonicalCharacterPaths.AbilityStrength:
                character.Abilities.Strength = value;
                break;
            case CanonicalCharacterPaths.AbilityDexterity:
                character.Abilities.Dexterity = value;
                break;
            case CanonicalCharacterPaths.AbilityConstitution:
                character.Abilities.Constitution = value;
                break;
            case CanonicalCharacterPaths.AbilityIntelligence:
                character.Abilities.Intelligence = value;
                break;
            case CanonicalCharacterPaths.AbilityWisdom:
                character.Abilities.Wisdom = value;
                break;
            case CanonicalCharacterPaths.AbilityCharisma:
                character.Abilities.Charisma = value;
                break;
            case CanonicalCharacterPaths.CombatArmorClass:
                character.Combat.ArmorClass = value;
                break;
            case CanonicalCharacterPaths.CombatMaxHitPoints:
                character.Combat.MaxHitPoints = value;
                break;
            case CanonicalCharacterPaths.CombatHitPoints:
                character.Combat.CurrentHitPoints = value;
                break;
            case CanonicalCharacterPaths.CombatSpeed:
                character.Combat.Speed = value;
                break;
            case CanonicalCharacterPaths.CombatProficiencyBonus:
                character.Combat.ProficiencyBonus = value;
                break;
        }
    }

    private static void ApplyText(string canonicalPath, string value, CanonicalCharacter character)
    {
        switch (canonicalPath)
        {
            case CanonicalCharacterPaths.IdentityName:
                character.Identity.Name = value;
                break;
            case CanonicalCharacterPaths.IdentityRaceOrAncestry:
                character.Identity.RaceOrAncestry = value;
                break;
            case CanonicalCharacterPaths.IdentityBackground:
                character.Identity.Background = value;
                break;
        }
    }
}
